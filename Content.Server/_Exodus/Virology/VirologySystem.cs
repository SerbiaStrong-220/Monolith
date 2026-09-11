// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared.GameTicking;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Rejuvenate;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Effects;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    /// <summary>Generic entity every virus strain spawns from, its symptoms/state come from descriptor.</summary>
    public const string BaseVirusProto = "BaseVirus";

    /// <summary>Max symptoms a normal strain can hold.</summary>
    public const int MaxSymptoms = 5;

    /// <summary>Max symptoms a supervirus can hold.</summary>
    public const int MaxSupervirusSymptoms = 7;

    /// <summary>How many reagents a supervirus cure needs (all present at once).</summary>
    public const int SupervirusCureCount = 3;

    /// <summary>How many reagents a normal (non-super) virus cure is rolled with.</summary>
    public const int NormalCureCount = 2;

    /// <summary>Cure pool the per-symptom accelerants are rolled from.</summary>
    private const string AccelerantPool = "Default";

    // reused when building a strain identity
    private readonly List<string> _identityBuf = [];

    // reused when picking a distinct accelerant
    private readonly List<ProtoId<ReagentPrototype>> _accelerantBuf = [];

    // reused when rolling a cure from pool
    private readonly List<ProtoId<ReagentPrototype>> _cureBuf = [];

    // cure rolled once per prototype per round
    private readonly Dictionary<EntProtoId, VirusCure?> _roundCures = [];

    public override void Initialize()
    {
        base.Initialize();
        _nextUpdate = _timing.CurTime;

        SubscribeLocalEvent<VirusHolderComponent, ComponentShutdown>(OnHolderShutdown);
        SubscribeLocalEvent<VirusHolderComponent, EntityUnpausedEvent>(OnHolderUnpaused);
        SubscribeLocalEvent<VirusComponent, ComponentShutdown>(OnVirusShutdown);
        SubscribeLocalEvent<VirusComponent, VirusDoseAbsorbedEvent>(OnDoseAbsorbed);
        SubscribeLocalEvent<VirusSusceptibleComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<VirusHolderComponent, EntityZombifiedEvent>(OnZombified);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        InitializeAggression();
        InitializeBlood();
        InitializeChemistry();
        InitializeEnvironment();
        InitializeImmunity();
        InitializeInfection();
        InitializeProgression();
        InitializeSpread();
        InitializeContamination();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<VirusMutationPrototype>() || args.WasModified<VirusRevealPrototype>()
            || args.WasModified<VirusSymptomRemovalPrototype>())
            BuildTables();

        if (args.WasModified<VirusBroadReagentPrototype>())
            BuildBroadReagents();
    }

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate += UpdateInterval;

        TickInfection();
        TickOverload();
        TickProgression();
        TickSpread();
        TickContamination();
    }

    // becoming zombie kills the host's viruses
    private void OnZombified(Entity<VirusHolderComponent> ent, ref EntityZombifiedEvent args)
    {
        foreach (var virus in GetStrains(ent))
            RemoveVirus(virus);
    }

    private void OnRejuvenate(Entity<VirusSusceptibleComponent> ent, ref RejuvenateEvent args)
    {
        foreach (var virus in GetStrains(ent))
            RemoveVirus(virus);

        RemComp<VirusImmunitiesComponent>(ent);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _roundCures.Clear();
    }

    public VirusCure? GetRoundCure(EntProtoId protoId)
    {
        if (_roundCures.TryGetValue(protoId, out var cached))
            return cached;

        var cure = RollCure(NormalCureCount);
        _roundCures[protoId] = cure;
        return cure;
    }

    public VirusCure? RollCure(int count)
    {
        if (!_proto.Resolve<VirusCurePoolPrototype>(AccelerantPool, out var pool))
            return null;

        _cureBuf.Clear();
        _cureBuf.AddRange(pool.Natural);
        _cureBuf.AddRange(pool.Synthesized);
        if (_cureBuf.Count == 0)
            return null;

        var cure = new VirusCure();
        cure.Reagents.AddRange(_random.GetItems(_cureBuf, count, allowDuplicates: false));
        return cure;
    }

    // absorbing an accelerant dose re-randomises which reagent drives that symptom so no insta 1->max stages
    private void OnDoseAbsorbed(Entity<VirusComponent> ent, ref VirusDoseAbsorbedEvent args)
    {
        args.Symptom.Accelerant = RollAccelerant(CollectAccelerants(ent.Comp, args.Symptom));
    }

    private void OnVirusShutdown(Entity<VirusComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Removing = true;
        if (TryComp<VirusHolderComponent>(ent.Comp.Carrier, out var holder) && !Terminating(ent.Comp.Carrier))
        {
            holder.Viruses.Remove(ent.Owner);
            ReconcileSymptoms((ent.Comp.Carrier, holder));
            RaiseContentsChanged(ent.Comp.Carrier);
        }
    }

    private void OnHolderShutdown(Entity<VirusHolderComponent> ent, ref ComponentShutdown args)
    {
        foreach (var virus in ent.Comp.Viruses)
            QueueDel(virus);

        ent.Comp.Viruses.Clear();
        if (Terminating(ent.Owner))
            return;

        ReconcileSymptoms(ent);
        Deactivate(ent.Owner);
    }

    private void OnHolderUnpaused(Entity<VirusHolderComponent> ent, ref EntityUnpausedEvent args)
    {
        foreach (var virus in EnumerateStrains(ent.Comp))
        {
            virus.Comp.SuppressedUntil += args.PausedTime;
            foreach (var state in virus.Comp.SymptomStates.Values)
            {
                state.StageStartTime += args.PausedTime;
                state.LastEmote += args.PausedTime;
            }
        }
    }

    #region Infection gate

    /// Infects a host a strain built from its prototype
    public bool AddVirus(EntityUid host, EntProtoId protoId)
    {
        return BuildDescriptor(protoId) is { } descriptor && AddVirus(host, descriptor);
    }

    public bool AddVirus(EntityUid host, VirusDescriptor descriptor)
    {
        if (Terminating(host) || !HasComp<VirusSusceptibleComponent>(host) || IsImmune(host)
            || descriptor.Symptoms.Count == 0)
            return false;

        var identity = GetIdentity(descriptor);

        if (TryComp<VirusImmunitiesComponent>(host, out var immunities) && immunities.Strains.Contains(identity))
            return false;

        Entity<VirusComponent>? mergeTarget = null;
        foreach (var carried in EnumerateStrains(host))
        {
            if (GetIdentity(carried.Comp) == identity)
                return false;

            // a different but compatible strain fuses into a supervirus
            if (!descriptor.IsSupervirus && !carried.Comp.IsSupervirus && carried.Comp.Genome == descriptor.Genome)
                mergeTarget = carried;
        }

        if (mergeTarget is { } target)
        {
            var targetVirus = target.Comp;

            // Immunity to the merged composition prevents fusion, but not infection by its individual strains.
            if (immunities != null && immunities.Strains.Contains(GetUnionIdentity(targetVirus, descriptor)))
                return SpawnVirus(host, descriptor) != null;

            if (targetVirus.SuppressedUntil != null)
                ReactivateVirus(target);

            return MergeDescriptor(target, descriptor);
        }

        return SpawnVirus(host, descriptor) != null;
    }

    public void InfectFromReagent(EntityUid host, ReagentId reagent, bool bloodborne = false)
    {
        if (VirusData.From(reagent) is not { Viruses.Count: > 0 } data)
            return;

        // Blood stamping and lab reactions replace metadata; existing descriptors remain read-only.
        foreach (var descriptor in data.Viruses)
        {
            if (!bloodborne && IsBloodOnly(descriptor))
                continue;

            AddVirus(host, descriptor);
        }
    }

    /// <summary>True if any of the strain's symptoms restrict it to blood-borne spread only.</summary>
    public bool IsBloodOnly(VirusComponent virus)
    {
        foreach (var symptomId in virus.SymptomStates.Keys)
        {
            if (_proto.Resolve(symptomId, out var symptom) && symptom.BloodBorneOnly)
                return true;
        }

        return false;
    }

    public bool IsBloodOnly(VirusDescriptor descriptor)
    {
        foreach (var snapshot in descriptor.Symptoms)
        {
            if (_proto.Resolve(snapshot.Symptom, out var symptom) && symptom.BloodBorneOnly)
                return true;
        }

        return false;
    }

    #endregion

    #region Suppression

    public void SuppressVirus(Entity<VirusComponent> virus, TimeSpan duration)
    {
        if (virus.Comp.Removing || duration <= TimeSpan.Zero)
            return;

        var wasActive = virus.Comp.SuppressedUntil == null;
        virus.Comp.SuppressedUntil = _timing.CurTime + duration;
        if (wasActive && TryComp<VirusHolderComponent>(virus.Comp.Carrier, out var holder))
        {
            ReconcileSymptoms((virus.Comp.Carrier, holder));
            _adminLog.Add(LogType.Virology, LogImpact.Low,
                $"Virus {DescribeVirus(virus.Comp)} on {ToPrettyString(virus.Comp.Carrier):target} was suppressed for {duration.TotalMinutes:0} min");
        }

        RaiseContentsChanged(virus.Comp.Carrier);
    }

    public void ReactivateVirus(Entity<VirusComponent> virus)
    {
        if (virus.Comp.SuppressedUntil == null)
            return;

        virus.Comp.SuppressedUntil = null;
        var now = _timing.CurTime;
        foreach (var (symptomId, state) in virus.Comp.SymptomStates)
        {
            state.LastEmote = now;
            state.EmoteDelay = TimeSpan.Zero;
        }

        RefreshSymptoms(virus);
        RaiseContentsChanged(virus.Comp.Carrier);
    }

    #endregion

    #region Stages

    public void RefreshSymptoms(Entity<VirusComponent> virus)
    {
        if (TryComp<VirusHolderComponent>(virus.Comp.Carrier, out var holder) && !Terminating(virus.Comp.Carrier))
            ReconcileSymptoms((virus.Comp.Carrier, holder));
    }

    public IVirusEffect[] BuildStageEffects(VirusSymptomPrototype symptom, int stage, EntityUid carrier)
    {
        if (stage < 0 || stage >= symptom.Stages.Length)
            return [];

        if (GetSpecies(carrier) is { } species && symptom.SpeciesOverrides.TryGetValue(species, out var over))
        {
            if (over.Immune || stage < over.MinStage || over.SuppressEffects)
                return [];
        }

        return symptom.Stages[stage].Effects;
    }

    public int ForceAdvanceAllSymptoms(EntityUid host)
    {
        var advanced = 0;
        foreach (var strain in EnumerateStrains(host))
        {
            var virus = strain.Comp;
            var advancedHere = 0;
            foreach (var (symptomId, state) in virus.SymptomStates)
            {
                if (!_proto.Resolve(symptomId, out var symptom) || state.Stage + 1 >= symptom.Stages.Length)
                    continue;

                state.Stage++;
                state.StageStartTime = _timing.CurTime;
                RefreshSymptoms(strain);

                var newStage = symptom.Stages[state.Stage];
                if (newStage.ProgressMessage is { } message)
                    VirusChat.SendSelfMessage(_chatManager, EntityManager, virus.Carrier, Loc.GetString(message), newStage.ProgressMessageColor);

                advancedHere++;
            }

            if (advancedHere > 0)
            {
                RaiseContentsChanged(virus.Carrier);
                advanced += advancedHere;
            }
        }

        return advanced;
    }

    private static ComponentRegistry BuildStageComponents(VirusSymptomPrototype symptom, int stage, ProtoId<SpeciesPrototype>? species)
    {
        if (stage < 0 || stage >= symptom.Stages.Length)
            return [];

        VirusSpeciesOverride? over = null;
        if (species is { } speciesId && symptom.SpeciesOverrides.TryGetValue(speciesId, out var found))
            over = found;

        if (over is { Immune: true })
            return [];

        if (over != null && stage < over.MinStage)
            return [];

        if (over?.ReplaceComponents is { } replace)
            return Copy(replace);

        var result = Copy(symptom.Stages[stage].Components);

        if (over != null)
        {
            foreach (var name in over.RemoveComponents)
                result.Remove(name);

            foreach (var (name, entry) in over.AddComponents)
                result[name] = entry;
        }

        return result;
    }

    private static ComponentRegistry Copy(ComponentRegistry source)
    {
        var copy = new ComponentRegistry();
        foreach (var (name, entry) in source)
            copy[name] = entry;

        return copy;
    }

    #endregion

    #region Symptom info

    public bool TryGetSymptomDescription(ProtoId<VirusSymptomPrototype> symptomId, out string? description)
    {
        description = null;
        if (!_proto.Resolve(symptomId, out var symptom))
            return false;

        foreach (var stage in symptom.Stages)
        {
            if (stage.Detection is not { } detection)
                continue;

            description = Loc.GetString(detection.Description);
            return true;
        }

        return false;
    }

    public string FormatSymptom(ProtoId<VirusSymptomPrototype> symptomId, string description, int stage, ProtoId<ReagentPrototype>? accelerant = null)
    {
        if (!_proto.Resolve(symptomId, out var symptom) || symptom.Stages.Length <= 1)
            return description;

        if (accelerant is { } acc && _proto.Resolve(acc, out var accProto))
        {
            return Loc.GetString("disease-diagnoser-symptom-stage",
                ("symptom", description),
                ("stage", stage + 1),
                ("max", symptom.Stages.Length),
                ("reagent", accProto.LocalizedName));
        }

        return Loc.GetString("pathology-symptom-stage",
            ("symptom", description),
            ("stage", stage + 1),
            ("max", symptom.Stages.Length));
    }

    #endregion
}
