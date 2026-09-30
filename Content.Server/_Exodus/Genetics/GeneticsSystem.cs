using System.Diagnostics.CodeAnalysis;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticsSystem : EntitySystem
{
    /// <summary>Notifies administrative viewers after a genome and its effects have been reconciled.</summary>
    public event Action<EntityUid>? GenomeUpdated;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private IAdminLogManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;

    public const int MaxBlockValue = 0xFFF;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GenomeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GenomeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GenomeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GenomeComponent, CloningEvent>(OnCloning);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public GeneticsRoundComponent GetRound()
    {
        // The round cipher intentionally lives outside maps.
        var query = AllEntityQuery<GeneticsRoundComponent>();
        while (query.MoveNext(out var roundUid, out var existing))
        {
            if (!TerminatingOrDeleted(roundUid) && existing.Context.Length != 0)
                return existing;
        }

        var uid = Spawn("GeneticsRound");
        var round = Comp<GeneticsRoundComponent>(uid);
        round.Context = Guid.NewGuid().ToString("N");
        round.BlockCount = Math.Clamp(round.BlockCount, 1, 50);
        foreach (var mutation in _prototypes.EnumeratePrototypes<GeneticMutationPrototype>())
        {
            if (mutation.ActivationThreshold < 1 || mutation.ActivationThreshold > MaxBlockValue ||
                (mutation.ActivationThreshold & 0xF) == 0 || (mutation.ActivationThreshold & 0xF0) == 0 ||
                (mutation.ActivationThreshold & 0xF00) == 0)
            {
                Log.Error($"Invalid genetics activation threshold: {mutation.ID}");
                continue;
            }
            round.Mutations.Add(mutation.ID);
        }
        _random.Shuffle(round.Mutations);
        if (round.Mutations.Count > round.BlockCount)
        {
            Log.Error($"More mutations than the {round.BlockCount} available genetic blocks; selecting a random subset.");
            round.Mutations.RemoveRange(round.BlockCount, round.Mutations.Count - round.BlockCount);
        }
        while (round.Mutations.Count < round.BlockCount)
            round.Mutations.Add(null);
        _random.Shuffle(round.Mutations);
        foreach (var id in round.Mutations)
        {
            // Empty blocks use the same initial value distribution as ordinary genes.
            round.Thresholds.Add(id is { } mutation ? (ushort) _prototypes.Index(mutation).ActivationThreshold : (ushort) 0xDAC);
        }
        return round;
    }

    public bool TryGetGenome(EntityUid uid, [NotNullWhen(true)] out GenomeComponent? genome)
    {
        genome = null;
        if (TerminatingOrDeleted(uid) || HasComp<GeneticIncompatibleComponent>(uid) ||
            !HasComp<BodyComponent>(uid) || !TryComp<HumanoidAppearanceComponent>(uid, out var appearance))
            return false;

        genome = EnsureComp<GenomeComponent>(uid);
        if (!genome.CapacityInitialized)
        {
            if (_prototypes.TryIndex(appearance.Species, out var species))
                genome.StabilityCapacity = Math.Max(0, species.GeneticStabilityCapacity);
            genome.CapacityInitialized = true;
        }
        var round = GetRound();
        if (genome.Context != round.Context || genome.Blocks.Count != round.Mutations.Count)
        {
            genome.Context = round.Context;
            genome.Blocks.Clear();
            foreach (var threshold in round.Thresholds)
            {
                ushort value;
                do
                {
                    value = (ushort) _random.Next(MaxBlockValue + 1);
                } while (IsBlockActive(value, threshold));
                genome.Blocks.Add(value);
            }
            foreach (var mutation in genome.InitialMutations)
            {
                var block = round.Mutations.IndexOf(mutation);
                if (block >= 0)
                    genome.Blocks[block] = (ushort) MaxBlockValue;
            }
            genome.Baseline = new List<ushort>(genome.Blocks);
            genome.Revision++;
            genome.EffectsInitialized = false;
        }
        if (!genome.EffectsInitialized)
            Reconcile((uid, genome));
        return true;
    }

    /// <summary>Critical patients are valid; dead bodies and non-mobs are not.</summary>
    public bool IsLivingSubject(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && TryComp<MobStateComponent>(uid, out var state) &&
               state.CurrentState is MobState.Alive or MobState.Critical;
    }

    public bool TryGetLivingGenome(EntityUid uid, [NotNullWhen(true)] out GenomeComponent? genome)
    {
        genome = null;
        return IsLivingSubject(uid) && TryGetGenome(uid, out genome);
    }

    /// <summary>All three digits must meet their own minimum. F00 does not satisfy D/A/C.</summary>
    public static bool IsBlockActive(int value, int threshold)
    {
        return (value & 0xF00) >= (threshold & 0xF00) &&
               (value & 0xF0) >= (threshold & 0xF0) &&
               (value & 0xF) >= (threshold & 0xF);
    }

    public bool TryRandomizeDigit(Entity<GenomeComponent> ent, int block, int digit, EntityUid actor)
    {
        if (!IsLivingSubject(ent) || block < 0 || block >= ent.Comp.Blocks.Count || digit is < 0 or > 2)
            return false;
        var shift = (2 - digit) * 4;
        var value = (ent.Comp.Blocks[block] & ~(0xF << shift)) | (_random.Next(16) << shift);
        return TrySetBlock(ent, block, value, actor);
    }

    /// <summary>Metabolized genostabilin disables every block without changing the round cipher or stored samples.</summary>
    public bool TryStabilize(EntityUid uid)
    {
        if (!IsLivingSubject(uid) || !TryComp<GenomeComponent>(uid, out var genome) ||
            !TryGetGenome(uid, out genome))
            return false;
        var changed = false;
        for (var i = 0; i < genome.Blocks.Count; i++)
        {
            changed |= genome.Blocks[i] != 0;
            genome.Blocks[i] = 0;
        }
        if (!changed)
            return true;
        genome.Revision++;
        Reconcile((uid, genome));
        _admin.Add(LogType.Action, LogImpact.High, $"Genostabilin reset the genome of {ToPrettyString(uid):target}");
        return true;
    }

    private void OnMapInit(Entity<GenomeComponent> ent, ref MapInitEvent args)
    {
        TryGetGenome(ent, out _);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = AllEntityQuery<GeneticsRoundComponent>();
        while (query.MoveNext(out var uid, out _))
            QueueDel(uid);
    }

    private void OnStartup(Entity<GenomeComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnShutdown(Entity<GenomeComponent> ent, ref ComponentShutdown args)
    {
        foreach (var action in ent.Comp.Actions.Values)
            RemoveOwnedAction(ent, action);
        ent.Comp.Actions.Clear();
        if (!TerminatingOrDeleted(ent))
            RemCompDeferred<GeneticEffectsComponent>(ent);
    }

    private void RemoveOwnedAction(EntityUid owner, EntityUid? action)
    {
        if (action is not { } uid || TerminatingOrDeleted(uid))
            return;
        // ActionsComponent may already have detached its actions during entity shutdown.
        if (_actions.TryGetActionData(uid, out var data) && data.AttachedEntity == owner)
            _actions.RemoveAction(owner, uid);
        QueueDel(uid);
    }

    public bool TrySetBlock(Entity<GenomeComponent> ent, int block, int value, EntityUid actor)
    {
        var round = GetRound();
        if (TerminatingOrDeleted(ent) || HasComp<GeneticIncompatibleComponent>(ent) ||
            ent.Comp.Context != round.Context || block < 0 || block >= ent.Comp.Blocks.Count || value < 0 || value > MaxBlockValue)
            return false;

        ent.Comp.Blocks[block] = (ushort) value;
        ent.Comp.Revision++;
        Reconcile(ent);
        _admin.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(actor):user} set genetic block {block + 1} to {value:X3} on {ToPrettyString(ent):target}");
        return true;
    }

    public bool TryApply(Entity<GenomeComponent> ent, GeneticSnapshot sample, EntityUid actor)
    {
        if (TerminatingOrDeleted(ent) || HasComp<GeneticIncompatibleComponent>(ent) ||
            !IsCompatible(sample) || ent.Comp.Context != sample.Context)
            return false;

        ent.Comp.Blocks = new List<ushort>(sample.Blocks);
        ent.Comp.Revision++;
        Reconcile(ent);
        _admin.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(actor):user} applied a genetic sample to {ToPrettyString(ent):target}");
        return true;
    }

    public bool IsCompatible(GeneticSnapshot sample)
    {
        var round = GetRound();
        if (sample.Context != round.Context || sample.Blocks.Count != round.Mutations.Count)
            return false;
        foreach (var block in sample.Blocks)
        {
            if (block > MaxBlockValue)
                return false;
        }
        return true;
    }

    public GeneticSnapshot Capture(Entity<GenomeComponent> ent)
    {
        return new GeneticSnapshot { Context = ent.Comp.Context, Blocks = new List<ushort>(ent.Comp.Blocks) };
    }

    public void Reconcile(Entity<GenomeComponent> ent)
    {
        ent.Comp.EffectsInitialized = true;
        var round = GetRound();
        var modifiers = new GeneticModifiers();
        var wanted = new HashSet<EntProtoId>();
        var active = new HashSet<ProtoId<GeneticMutationPrototype>>();
        var periodic = new DamageSpecifier();
        var load = 0;
        for (var i = 0; i < ent.Comp.Blocks.Count && i < round.Mutations.Count; i++)
        {
            if (ent.Comp.Context != round.Context || round.Mutations[i] is not { } id ||
                !IsBlockActive(ent.Comp.Blocks[i], round.Thresholds[i]))
                continue;
            var mutation = _prototypes.Index(id);
            active.Add(mutation.ID);
            load += mutation.Instability;
            periodic += mutation.PeriodicDamage;
            var source = mutation.Modifiers;
            if (source.Transformation is { } transformation &&
                (modifiers.Transformation == null || string.CompareOrdinal(transformation.Id, modifiers.Transformation.Value.Id) < 0))
                modifiers.Transformation = transformation;
            modifiers.NoBreathing |= source.NoBreathing;
            modifiers.LowPressureImmunity |= source.LowPressureImmunity;
            modifiers.HighPressureImmunity |= source.HighPressureImmunity;
            modifiers.ColdImmunity |= source.ColdImmunity;
            modifiers.HeatImmunity |= source.HeatImmunity;
            modifiers.MovementMultiplier *= source.MovementMultiplier;
            modifiers.MeleeMultiplier *= source.MeleeMultiplier;
            if (source.UnarmedDamage is { } unarmedDamage)
            {
                modifiers.UnarmedDamage = modifiers.UnarmedDamage == null
                    ? new DamageSpecifier(unarmedDamage)
                    : modifiers.UnarmedDamage + unarmedDamage;
            }
            modifiers.StaminaMultiplier *= source.StaminaMultiplier;
            modifiers.DamageMultiplier *= source.DamageMultiplier;
            foreach (var (type, reduction) in source.DamageModifiers.FlatReduction)
            {
                modifiers.DamageModifiers.FlatReduction.TryGetValue(type, out var previous);
                modifiers.DamageModifiers.FlatReduction[type] = previous + reduction;
            }
            foreach (var (type, coefficient) in source.DamageModifiers.Coefficients)
            {
                modifiers.DamageModifiers.Coefficients[type] = modifiers.DamageModifiers.Coefficients.TryGetValue(type, out var previous)
                    ? previous * coefficient : coefficient;
            }
            modifiers.SizeMultiplier *= source.SizeMultiplier;
            modifiers.BlockRangedWeapons |= source.BlockRangedWeapons;
            modifiers.NutritionMultiplier *= source.NutritionMultiplier;
            modifiers.ThirstMultiplier *= source.ThirstMultiplier;
            modifiers.ConductivityMultiplier *= source.ConductivityMultiplier;
            modifiers.BleedingMultiplier *= source.BleedingMultiplier;
            modifiers.FlashDurationMultiplier *= source.FlashDurationMultiplier;
            modifiers.PhotophobiaStrength = Math.Max(modifiers.PhotophobiaStrength, source.PhotophobiaStrength);
            modifiers.ClottingRate += source.ClottingRate;
            modifiers.NutritionDrain += source.NutritionDrain;
            modifiers.BlockedStatuses.UnionWith(source.BlockedStatuses);
            modifiers.Abilities |= source.Abilities;
            wanted.UnionWith(mutation.Actions);
        }

        var removed = new List<EntProtoId>();
        foreach (var (id, action) in ent.Comp.Actions)
        {
            if (wanted.Contains(id))
                continue;
            RemoveOwnedAction(ent, action);
            removed.Add(id);
        }
        foreach (var id in removed)
            ent.Comp.Actions.Remove(id);
        foreach (var id in wanted)
        {
            if (ent.Comp.Actions.TryGetValue(id, out var existing) && existing is { } uid && !TerminatingOrDeleted(uid))
                continue;
            EntityUid? action = null;
            if (_actions.AddAction(ent, ref action, id))
                ent.Comp.Actions[id] = action;
        }

        foreach (var old in ent.Comp.Active)
        {
            if (active.Contains(old))
                continue;
            SendSensation(ent, "genetics-feeling-loss");
            break;
        }
        foreach (var id in active)
        {
            if (!ent.Comp.Active.Contains(id))
                SendSensation(ent, _prototypes.Index(id).ActivationMessage);
        }
        ent.Comp.Active = active;
        ent.Comp.PeriodicDamage = periodic;
        ent.Comp.Stability = ent.Comp.StabilityCapacity - load;
        var effects = EnsureComp<GeneticEffectsComponent>(ent);
        effects.Modifiers = modifiers;
        effects.Reverting = false;
        Dirty(ent, effects);
        _movement.RefreshMovementSpeedModifiers(ent);

        // Start the adaptation from the body's normal range, without repeatedly overwriting its temperature.
        if (TryComp<TemperatureComponent>(ent, out var temperature) &&
            (modifiers.ColdImmunity && temperature.CurrentTemperature < temperature.ColdDamageThreshold ||
             modifiers.HeatImmunity && temperature.CurrentTemperature > temperature.HeatDamageThreshold))
            _temperature.ForceChangeTemperature(ent, (temperature.ColdDamageThreshold + temperature.HeatDamageThreshold) / 2, temperature);

        var changed = new GenomeChangedEvent();
        RaiseLocalEvent(ent, ref changed);
        GenomeUpdated?.Invoke(ent.Owner);
    }

    private void SendSensation(EntityUid uid, LocId message)
    {
        if (!IsLivingSubject(uid) || !TryComp<ActorComponent>(uid, out var actor))
            return;
        var text = Loc.GetString(message);
        var wrapped = Loc.GetString("chat-manager-server-wrap-message", ("message", text));
        _chat.ChatMessageToOne(ChatChannel.Emotes, text, wrapped, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }

    private void OnCloning(Entity<GenomeComponent> ent, ref CloningEvent args)
    {
        if (!TryGetGenome(args.Target, out var genome))
            return;
        genome.Baseline = new List<ushort>(ent.Comp.Baseline);
        TryApply((args.Target, genome), Capture(ent), ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GenomeComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var genome, out var mob))
        {
            if (genome.NextUpdate > _timing.CurTime)
                continue;
            genome.NextUpdate += genome.Interval;
            if (mob.CurrentState == MobState.Dead)
                continue;
            UpdatePhysiology(uid, (float) genome.Interval.TotalSeconds);
            if (!genome.PeriodicDamage.Empty)
                _damage.TryChangeDamage(uid, genome.PeriodicDamage, true, false);
            if (genome.Stability < 0)
                _damage.TryChangeDamage(uid, genome.InstabilityDamage * Math.Min(5, 1 + -genome.Stability / 20), true, false);
        }
    }
}
