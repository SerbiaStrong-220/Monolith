using System.Numerics;
using Content.Server.Destructible;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Physics;
using Content.Shared.Mobs.Components;
using Content.Shared.SubFloor;
using Content.Shared.Whitelist;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotSpreadSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private DamageableSystem _damage = default!;
    private EntityQuery<PhysicsComponent> _physics;
    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<DamageableComponent> _damageQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<SubFloorHideComponent> _subFloorQuery;
    private readonly HashSet<EntityUid> _occupants = [];

    private void InitializeConversion()
    {
        _physics = GetEntityQuery<PhysicsComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _damageQuery = GetEntityQuery<DamageableComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _subFloorQuery = GetEntityQuery<SubFloorHideComponent>();
        SubscribeLocalEvent<RotSpreadComponent, RotSpreadConversionEvent>(OnConversionDone);
    }

    private RotGrowthCell Classify(Entity<RotSpreadComponent> ent,
        Entity<RotIntelligentComponent, RotColonyStateComponent> core, Entity<MapGridComponent> grid,
        Vector2i tile, out EntityUid? target)
    {
        target = null;
        if (_turf.IsSpace(_maps.GetTileRef(grid, grid.Comp, tile)) || core.Comp2.Reservations.ContainsKey(tile)
            || _grids.TryComp(grid, out var registry) && registry.Reservations.TryGetValue(tile, out var owner) && owner != ent.Owner)
            return RotGrowthCell.Blocked;
        var existing = core.Comp2.Cells.ContainsKey(tile);
        EntityUid? obstacle = null;
        var point = _maps.GridTileToLocal(grid, grid.Comp, tile);
        _occupants.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, Box2.CenteredAround(point.Position, new Vector2(0.9f)), _occupants,
            flags: LookupFlags.Uncontained);
        foreach (var uid in _occupants)
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid)
                || uid == ent.Comp.PendingMarker)
                continue;
            // Subfloor utilities occupy a separate layer even when their floor tiles are removed.
            if (_subFloorQuery.TryComp(uid, out var subFloor) && subFloor.BlockInteractions)
                continue;
            if (_members.TryComp(uid, out var member))
            {
                if (member.Size == Vector2i.Zero)
                    continue;
                if (member.Core != core.Owner)
                    return RotGrowthCell.Blocked;
                existing = true;
                continue;
            }
            if (_whitelist.IsValid(ent.Comp.Ignored, uid))
                continue;
            var blocking = _physics.TryComp(uid, out var physics) && physics.CanCollide && physics.Hard
                && (physics.CollisionLayer & (int)CollisionGroup.FullTileMask) != 0;
            if (_mobQuery.HasComp(uid) || !Transform(uid).Anchored && !blocking)
                continue;
            // Docking ports are left intact instead of being eaten through into space.
            if (_whitelist.IsValid(ent.Comp.Excluded, uid))
                return RotGrowthCell.Blocked;
            var door = _doorQuery.HasComp(uid);
            var wall = _whitelist.IsValid(ent.Comp.ConvertibleWalls, uid);
            var destructible = _damageQuery.HasComp(uid) && _destructible.DestroyedAt(uid) != FixedPoint2.MaxValue;
            if (Transform(uid).Anchored && (door || wall) && destructible)
            {
                if (target != null)
                    return RotGrowthCell.Blocked;
                target = uid;
            }
            else if (destructible)
                obstacle ??= uid;
            else if (blocking)
                return RotGrowthCell.Blocked;
        }
        if (obstacle is { } damageTarget)
        {
            target = damageTarget;
            return RotGrowthCell.Destructible;
        }
        return target != null ? RotGrowthCell.Convertible : existing ? RotGrowthCell.Existing : RotGrowthCell.Empty;
    }

    private void Corrode(Entity<RotSpreadComponent> ent, EntityUid target, EntityUid core)
    {
        // Damage and destruction use the normal game systems; the shared cooldown lives on the target.
        var corrosion = EnsureComp<RotCorrosionComponent>(target);
        if (_timing.CurTime < corrosion.NextDamage)
            return;
        var interval = ent.Comp.AttackInterval > TimeSpan.Zero ? ent.Comp.AttackInterval : TimeSpan.FromSeconds(0.1);
        corrosion.NextDamage = _timing.CurTime + interval;
        _damage.TryChangeDamage(target, ent.Comp.ObstacleDamage, origin: core);
        LastGrowth++;
    }

    private void BeginConversion(Entity<RotSpreadComponent> ent, Entity<MapGridComponent> grid, Vector2i tile, EntityUid target)
    {
        var source = ent.Comp;
        var index = EnsureComp<RotSpreadGridComponent>(grid);
        if (!index.Reservations.TryAdd(tile, ent.Owner) || !_damageQuery.TryComp(target, out var damage))
            return;
        var remaining = Math.Max(0, _destructible.DestroyedAt(target).Float() - damage.TotalDamage.Float());
        var duration = source.ConversionDuration + TimeSpan.FromSeconds(remaining * Math.Max(0, source.ConversionSecondsPerDamage));
        var marker = Spawn(source.Marker, _maps.GridTileToLocal(grid, grid.Comp, tile));
        source.PendingMarker = marker;
        source.Target = target;
        source.TargetTile = tile;
        var args = new DoAfterArgs(EntityManager, marker, duration, new RotSpreadConversionEvent(), ent, target: target)
        {
            NeedHand = false,
            BreakOnMove = false,
            BreakOnDamage = false,
            MultiplyDelay = false,
            CancelDuplicate = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out source.Conversion))
            CancelConversion(ent);
        LastGrowth++;
    }

    private void OnConversionDone(Entity<RotSpreadComponent> ent, ref RotSpreadConversionEvent args)
    {
        if (args.Handled || ent.Comp.Conversion != args.DoAfter.Id)
            return;
        args.Handled = true;
        ent.Comp.Conversion = null;
        if (args.Cancelled)
            CancelConversion(ent);
        else
            ent.Comp.ConversionReady = true;
    }

    private void FinishConversion(Entity<RotSpreadComponent> ent,
        Entity<RotIntelligentComponent, RotColonyStateComponent> core, Entity<MapGridComponent> grid)
    {
        var source = ent.Comp;
        var tile = source.TargetTile;
        if (source.Target is not { } target || TerminatingOrDeleted(target)
            || Classify(ent, core, grid, tile, out var actual) != RotGrowthCell.Convertible || actual != target
            || core.Comp2.Cells.Count >= core.Comp1.MaxTerritory)
        {
            CancelConversion(ent);
            return;
        }
        var isDoor = _doorQuery.HasComp(target);
        var open = isDoor && _physics.TryComp(target, out var physics) && !physics.CanCollide;
        // Both entities exist during replacement so closed hulls never expose a temporary hole to atmos.
        var built = Spawn(isDoor ? source.Door : source.Wall, _maps.GridTileToLocal(grid, grid.Comp, tile));
        _transform.SetLocalRotation(built, Transform(target).LocalRotation);
        if (open)
        {
            _doors.OnPartialOpen(built);
            _doors.SetState(built, DoorState.Open);
            _doors.SetNextStateChange(built, null);
        }
        _colony.Join(built, core);
        _colony.CopyColonyStrain(core, built);
        // Free conversions keep the default zero salvage value and never acquire a spreader component.
        QueueDel(target);
        CancelConversion(ent);
        source.Rebuild = true;
        LastGrowth++;
        Delay(source);
    }

    private void CancelConversion(Entity<RotSpreadComponent> ent)
    {
        var source = ent.Comp;
        var conversion = source.Conversion;
        source.Conversion = null;
        if (conversion != null)
            _doAfter.Cancel(conversion);
        if (source.Grid is { } grid && _grids.TryComp(grid, out var index)
            && index.Reservations.TryGetValue(source.TargetTile, out var owner) && owner == ent.Owner)
        {
            index.Reservations.Remove(source.TargetTile);
            Wake(grid, source.TargetTile);
        }
        if (source.PendingMarker is { } marker && !TerminatingOrDeleted(marker))
            QueueDel(marker);
        source.PendingMarker = null;
        source.Target = null;
        source.ConversionReady = false;
    }
}

public enum RotGrowthCell : byte
{
    Blocked,
    Empty,
    Existing,
    Convertible,
    Destructible,
}
