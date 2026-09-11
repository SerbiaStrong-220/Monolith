using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Data-driven corpse incubation, infectious offspring and environmental reservoirs.</summary>
public sealed partial class VirusLifecycleSystem : EntitySystem
{
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private TimeSpan _nextUpdate;
    private TimeSpan _nextExposure;
    private readonly HashSet<Entity<VirusSusceptibleComponent>> _nearby = [];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VirusBroodComponent, ComponentStartup>(OnBroodStartup);
        SubscribeLocalEvent<VirusBroodComponent, MobStateChangedEvent>(OnBroodMobState);
        SubscribeLocalEvent<VirusBroodComponent, ExaminedEvent>(OnBroodExamined);
        InitializeOffspring();
        InitializeReservoirs();
    }

    private void OnBroodStartup(Entity<VirusBroodComponent> ent, ref ComponentStartup args)
    {
        if (_mobState.IsDead(ent))
            BeginIncubation(ent);
    }

    private void OnBroodMobState(Entity<VirusBroodComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            BeginIncubation(ent);
        else
            ent.Comp.Incubating = false;
    }

    private void BeginIncubation(Entity<VirusBroodComponent> ent)
    {
        if (ent.Comp.Incubating || ent.Comp.Hatched)
            return;

        ent.Comp.Incubating = true;
        ent.Comp.Remaining = ent.Comp.MinDelay + (ent.Comp.MaxDelay - ent.Comp.MinDelay) * _random.NextFloat();
        ent.Comp.LastUpdate = _timing.CurTime;
    }

    private void OnBroodExamined(Entity<VirusBroodComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Incubating && ent.Comp.IncubatingMessage is { } message)
            args.PushMarkup(Loc.GetString(message));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + TimeSpan.FromSeconds(1);
        var broods = EntityQueryEnumerator<VirusBroodComponent, BodyComponent>();
        while (broods.MoveNext(out var uid, out var brood, out _))
        {
            if (!brood.Incubating || brood.Hatched || TerminatingOrDeleted(uid)
                || EntityManager.IsQueuedForDeletion(uid))
                continue;

            var elapsed = now - brood.LastUpdate;
            brood.LastUpdate = now;
            if (!_mobState.IsDead(uid))
            {
                brood.Incubating = false;
                continue;
            }

            // A corpse in a locker, bag or morgue cannot hatch through its container.
            if (_containers.IsEntityInContainer(uid) || !_rotting.IsRotProgressing(uid, null))
                continue;

            brood.Remaining -= elapsed;
            if (brood.Remaining <= TimeSpan.Zero)
                TryHatch((uid, brood));
        }

        UpdateOffspring(now);
        if (now < _nextExposure)
            return;

        _nextExposure = now + TimeSpan.FromSeconds(10);
        ExposeReservoirs();
    }

    private void TryHatch(Entity<VirusBroodComponent> ent)
    {
        VirusDescriptor? descriptor = null;
        foreach (var strain in _virology.EnumerateStrains(ent.Owner))
        {
            if (strain.Comp.SuppressedUntil == null && strain.Comp.SymptomStates.ContainsKey(ent.Comp.Symptom))
            {
                descriptor = FreshInfection(_virology.ToDescriptor(strain));
                break;
            }
        }

        if (descriptor == null || Transform(ent).MapUid == null)
            return;

        var coordinates = Transform(ent).Coordinates;
        var count = GetOffspringCount(ent);
        var offspring = ent.Comp.Offspring;
        var burst = ent.Comp.BurstEffect;
        ent.Comp.Hatched = true;
        _body.GibBody(ent.Owner);
        if (!EntityManager.IsQueuedForDeletion(ent.Owner))
        {
            ent.Comp.Hatched = false;
            return;
        }

        if (burst is { } effect)
            Spawn(effect, coordinates);

        for (var i = 0; i < count; i++)
        {
            var child = Spawn(offspring, coordinates);
            var vector = EnsureComp<VirusOffspringComponent>(child);
            vector.Strain = descriptor.Clone();
            vector.ExpiresAt = _timing.CurTime + vector.Lifetime;
        }
    }

    private int GetOffspringCount(Entity<VirusBroodComponent> ent)
    {
        if (ent.Comp.HealthPerOffspring <= 0)
        {
            Log.Error($"Virus brood on {ToPrettyString(ent)} has non-positive healthPerOffspring.");
            return 1;
        }

        if (!_mobThresholds.TryGetThresholdForState(ent, MobState.Dead, out var threshold))
            return 1;

        // Fixed-point division would truncate fractional health above a boundary before the ceiling.
        return Math.Max(1, (int)Math.Ceiling(threshold.Value.Double() / ent.Comp.HealthPerOffspring.Double()));
    }

    /// <summary>Preserve the strain and its cure, but give each newly infected host its own incubation.</summary>
    public static VirusDescriptor FreshInfection(VirusDescriptor source)
    {
        var copy = source.Clone();
        copy.SuppressedRemaining = null;
        foreach (var symptom in copy.Symptoms)
        {
            symptom.Stage = 0;
            symptom.Revealed = false;
        }

        return copy;
    }
}
