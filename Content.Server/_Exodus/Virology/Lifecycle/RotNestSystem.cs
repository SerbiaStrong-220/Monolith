using Content.Server.Spreader;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.EntityTable;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotNestSystem : EntitySystem
{
    [Dependency] private VirusLifecycleSystem _lifecycle = default!;
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private EntityTableSystem _tables = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly HashSet<Entity<BodyComponent>> _bodies = [];
    private readonly List<EntityUid> _removed = [];
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        InitializeFeeding();
        SubscribeLocalEvent<RotNestComponent, MapInitEvent>(OnNestInit);
        SubscribeLocalEvent<RotLarvaComponent, MapInitEvent>(OnLarvaInit);
        SubscribeLocalEvent<RotLarvaComponent, ComponentShutdown>(OnLarvaShutdown);
        SubscribeLocalEvent<RotLarvaComponent, MobStateChangedEvent>(OnLarvaState);
        SubscribeLocalEvent<RotLarvaComponent, ExaminedEvent>(OnLarvaExamined);
        SubscribeLocalEvent<RotLarvaComponent, RefreshMovementSpeedModifiersEvent>(OnLarvaSpeed);
    }

    private void OnLarvaSpeed(Entity<RotLarvaComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.HatchAt != null)
            args.ModifySpeed(0f, 0f);
    }

    private void OnNestInit(Entity<RotNestComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpawn = _timing.CurTime + ent.Comp.SpawnInterval;
        ent.Comp.SelectedVines ??= new(_tables.GetSpawns(ent.Comp.Vines));
    }

    private void OnLarvaInit(Entity<RotLarvaComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Strain ??= _virology.BuildDescriptor(ent.Comp.InitialVirus);
        UpdateLarvaVisual(ent);
    }

    private void OnLarvaExamined(Entity<RotLarvaComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.HatchAt != null ? "rot-larva-sated"
            : ent.Comp.Satiety >= ent.Comp.MaxSatiety ? "rot-larva-seeking-shelter" : "rot-larva-satiety",
            ("current", ent.Comp.Satiety), ("maximum", ent.Comp.MaxSatiety)));
    }

    private void OnLarvaShutdown(Entity<RotLarvaComponent> ent, ref ComponentShutdown args)
    {
        CancelFeeding(ent);
        CancelShelter(ent);
        ReleaseNest(ent);
    }

    private void OnLarvaState(Entity<RotLarvaComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            CancelFeeding(ent);
            CancelShelter(ent);
            ent.Comp.PupateBy = null;
            ent.Comp.HatchAt = null;
            _movement.RefreshMovementSpeedModifiers(ent);
            ReleaseNest(ent);
        }
        UpdateLarvaVisual(ent);
    }

    private void ReleaseNest(Entity<RotLarvaComponent> ent)
    {
        if (ent.Comp.Nest is { } nest && TryComp<RotNestComponent>(nest, out var colony))
            colony.Larvae.Remove(ent);
        ent.Comp.Nest = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;
        _nextUpdate = now + TimeSpan.FromSeconds(1);

        var nests = EntityQueryEnumerator<RotNestComponent, VirusReservoirComponent>();
        while (nests.MoveNext(out var uid, out var nest, out var reservoir))
        {
            if (TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid) || Transform(uid).GridUid == null)
                continue;

            if (!nest.Seeded)
                SeedVines((uid, nest));
            if (now < nest.NextSpawn || reservoir.Strain == null)
                continue;

            nest.NextSpawn = now + nest.SpawnInterval;
            _removed.Clear();
            foreach (var child in nest.Larvae)
            {
                if (TerminatingOrDeleted(child) || _mobs.IsDead(child))
                    _removed.Add(child);
            }
            foreach (var child in _removed)
                nest.Larvae.Remove(child);
            if (nest.Larvae.Count >= nest.Capacity)
                continue;

            var larva = Spawn(nest.Larva, Transform(uid).Coordinates);
            var spawned = new VirusOffspringSpawnedEvent(larva);
            RaiseLocalEvent(uid, ref spawned);
            if (!TryComp<RotLarvaComponent>(larva, out var larvaComponent))
            {
                QueueDel(larva);
                continue;
            }
            larvaComponent.Nest = uid;
            larvaComponent.Strain = VirusLifecycleSystem.FreshInfection(reservoir.Strain);
            nest.Larvae.Add(larva);
        }

        var larvae = EntityQueryEnumerator<RotLarvaComponent>();
        while (larvae.MoveNext(out var uid, out var larva))
        {
            if (larva.Satiety >= larva.MaxSatiety && larva.HatchAt == null
                && !TerminatingOrDeleted(uid) && !_mobs.IsDead(uid) && !_containers.IsEntityInContainer(uid))
                UpdateShelter((uid, larva));
            if (larva.HatchAt is not { } hatch || now < hatch || larva.Strain == null
                || TerminatingOrDeleted(uid) || _mobs.IsDead(uid) || _containers.IsEntityInContainer(uid))
                continue;
            larva.HatchAt = null;
            ReleaseNest((uid, larva));
            _lifecycle.SpawnOffspring(Transform(uid).Coordinates, larva.Strain, larva.Offspring, larva.OffspringTable, uid);
            QueueDel(uid);
        }
    }

    private void SeedVines(Entity<RotNestComponent> ent)
    {
        var transform = Transform(ent);
        if (transform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;
        var indices = _map.TileIndicesFor(grid, mapGrid, transform.Coordinates);
        if (_turf.IsSpace(_map.GetTileRef(grid, mapGrid, indices)))
            return;

        _removed.Clear();
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, mapGrid, indices);
        while (anchored.MoveNext(out var other))
        {
            if (!HasComp<EdgeSpreaderComponent>(other))
                continue;
            if (ent.Comp.ReplaceableVines == null || !_whitelist.IsValid(ent.Comp.ReplaceableVines, other.Value))
                return;

            _removed.Add(other.Value);
        }

        // A previous colony's ground cover must not override this nest's independently selected vine.
        foreach (var vine in _removed)
            QueueDel(vine);
        ent.Comp.SelectedVines ??= new(_tables.GetSpawns(ent.Comp.Vines));
        foreach (var prototype in ent.Comp.SelectedVines)
            Spawn(prototype, _map.GridTileToLocal(grid, mapGrid, indices));
        ent.Comp.Seeded = true;
    }
}
