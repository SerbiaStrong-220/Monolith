using System.Numerics;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Server.Spreader;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.EntityTable;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.FixedPoint;
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
        SubscribeLocalEvent<RotNestComponent, MapInitEvent>(OnNestInit);
        SubscribeLocalEvent<RotLarvaComponent, MapInitEvent>(OnLarvaInit);
        SubscribeLocalEvent<RotLarvaComponent, EntityTerminatingEvent>(OnLarvaTerminating);
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
    }

    private void OnLarvaExamined(Entity<RotLarvaComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.HatchAt != null ? "rot-larva-sated" : "rot-larva-satiety",
            ("current", ent.Comp.Satiety), ("maximum", ent.Comp.MaxSatiety)));
    }

    private void OnLarvaTerminating(Entity<RotLarvaComponent> ent, ref EntityTerminatingEvent args) => ReleaseNest(ent);

    private void OnLarvaState(Entity<RotLarvaComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            ent.Comp.HatchAt = null;
            _movement.RefreshMovementSpeedModifiers(ent);
            ReleaseNest(ent);
        }
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
            if (larva.HatchAt is not { } hatch || now < hatch || larva.Strain == null
                || TerminatingOrDeleted(uid) || _mobs.IsDead(uid) || _containers.IsEntityInContainer(uid))
                continue;
            larva.HatchAt = null;
            ReleaseNest((uid, larva));
            _lifecycle.SpawnOffspring(Transform(uid).Coordinates, larva.Strain, larva.Offspring, larva.OffspringTable);
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

    public bool TryFindFood(Entity<RotLarvaComponent> ent, out EntityUid target)
    {
        target = default;
        if (ent.Comp.Satiety >= ent.Comp.MaxSatiety || _mobs.IsDead(ent) || _containers.IsEntityInContainer(ent))
            return false;

        var origin = _transform.GetMapCoordinates(ent);
        var closest = float.MaxValue;
        _bodies.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.SearchRange, _bodies);
        foreach (var (uid, _) in _bodies)
        {
            if (!CanEat(uid))
                continue;
            var position = _transform.GetMapCoordinates(uid);
            var distance = Vector2.DistanceSquared(origin.Position, position.Position);
            if (distance >= closest || !_interaction.InRangeUnobstructed(ent.Owner, uid, ent.Comp.SearchRange))
                continue;
            closest = distance;
            target = uid;
        }
        return target.IsValid();
    }

    public bool CanEat(EntityUid uid) => !TerminatingOrDeleted(uid) && _mobs.IsDead(uid)
        && HasComp<BodyComponent>(uid) && !HasComp<RotCreatureComponent>(uid) && !_containers.IsEntityInContainer(uid)
        && (!TryComp<RotCorpseNutritionComponent>(uid, out var food) || food.Remaining > 0)
        && (!TryComp<RotCorpseClaimComponent>(uid, out var claim) || TerminatingOrDeleted(claim.Consumer))
        && (HasEdibleLimb(uid) || HasBlood(uid));

    private bool HasBlood(EntityUid uid) => TryComp<BloodstreamComponent>(uid, out var blood)
        && _solutions.ResolveSolution(uid, blood.BloodSolutionName, ref blood.BloodSolution, out var solution)
        && solution.Volume > FixedPoint2.Zero;

    private bool HasEdibleLimb(EntityUid uid)
    {
        foreach (var (partUid, part) in _body.GetBodyChildren(uid))
        {
            if (part.PartType is BodyPartType.Arm or BodyPartType.Leg
                && _body.GetParentPartAndSlotOrNull(partUid) != null)
                return true;
        }
        return false;
    }

    public bool TryFeed(Entity<RotLarvaComponent> ent, EntityUid corpse)
    {
        // HTN calls Update every tick; range and container checks are only needed for an actual bite.
        if (ent.Comp.NextBite is { } next && _timing.CurTime < next)
            return true;

        if (!CanEat(corpse) || _mobs.IsDead(ent) || ent.Comp.Satiety >= ent.Comp.MaxSatiety
            || _containers.IsEntityInContainer(ent) || !_interaction.InRangeUnobstructed(ent.Owner, corpse, 1.2f))
        {
            ent.Comp.NextBite = null;
            return false;
        }
        ent.Comp.NextBite ??= _timing.CurTime + ent.Comp.BiteInterval;
        if (_timing.CurTime < ent.Comp.NextBite)
            return true;
        ent.Comp.NextBite = _timing.CurTime + ent.Comp.BiteInterval;

        if (!TryComp<RotCorpseNutritionComponent>(corpse, out var food))
        {
            food = AddComp<RotCorpseNutritionComponent>(corpse);
            food.Remaining = ent.Comp.MaxSatiety * ent.Comp.MealsPerCorpse;
        }
        if (food.Remaining <= 0)
            return false;

        // Drink a share of the actual blood solution without creating a new puddle.
        if (TryComp<BloodstreamComponent>(corpse, out var blood)
            && _solutions.ResolveSolution(corpse, blood.BloodSolutionName, ref blood.BloodSolution, out var solution))
            _solutions.SplitSolution(blood.BloodSolution.Value,
                FixedPoint2.Max(FixedPoint2.New(0.001), solution.Volume / food.Remaining));

        food.Remaining--;
        ent.Comp.Satiety++;
        if (ent.Comp.Satiety < ent.Comp.MaxSatiety)
            return true;

        ConsumeLimb(corpse);
        ent.Comp.HatchAt = _timing.CurTime + ent.Comp.HatchDelay;
        _steering.Unregister(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
        _appearance.SetData(ent, RotLarvaVisuals.Sated, true);
        if (ent.Comp.PupaName is { } name)
            _metadata.SetEntityName(ent, Loc.GetString(name));
        return true;
    }

    private void ConsumeLimb(EntityUid corpse)
    {
        foreach (var (uid, part) in _body.GetBodyChildren(corpse))
        {
            if (part.PartType is not (BodyPartType.Arm or BodyPartType.Leg)
                || _body.GetParentPartAndSlotOrNull(uid) is not { } parent)
                continue;
            if (_body.DetachPart(parent.Parent, parent.Slot, uid))
                QueueDel(uid);
            break;
        }
    }
}
