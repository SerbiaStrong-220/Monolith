using System.Numerics;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Prototypes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    private readonly HashSet<EntityUid> _repairDebris = new();
    private readonly HashSet<EntityUid> _repairPlacementHits = new();
    private readonly List<(EntityUid Item, EntityCoordinates Position)> _repairDebrisMoves = new();

    private bool IsMovableRepairDebris(EntityUid uid)
    {
        return !_repairMobQuery.HasComponent(uid) &&
               _repairTransformQuery.TryGetComponent(uid, out var xform) &&
               (_repairDebrisQuery.HasComponent(uid) || _repairItemQuery.HasComponent(uid) && !xform.Anchored);
    }

    private static bool RepairFixturesOverlap(Fixture first, PhysicsTransform firstPosition,
        Fixture second, PhysicsTransform secondPosition)
    {
        // Body-wide masks also contain sensors (for example a banana peel's slippery fixture).
        if (!first.Hard || !second.Hard ||
            (first.CollisionMask & second.CollisionLayer) == 0 &&
            (first.CollisionLayer & second.CollisionMask) == 0)
            return false;
        for (var i = 0; i < first.Shape.ChildCount; i++)
        for (var j = 0; j < second.Shape.ChildCount; j++)
        {
            if (first.Shape.ComputeAABB(firstPosition, i).Intersects(second.Shape.ComputeAABB(secondPosition, j)))
                return true;
        }
        return false;
    }

    private bool RepairFixtureIntersectsBody(Fixture fixture, PhysicsTransform placement, EntityUid other,
        FixturesComponent fixtures)
    {
        var otherPosition = new PhysicsTransform(_transform.GetWorldPosition(other), _transform.GetWorldRotation(other));
        foreach (var occupied in fixtures.Fixtures.Values)
        {
            if (RepairFixturesOverlap(fixture, placement, occupied, otherPosition))
                return true;
        }
        return false;
    }

    private bool TryMoveRepairDebris(Entity<ShipRepairDataComponent> grid, Vector2i tile, ShipRepairChunk chunk,
        ShipRepairEntitySpecifier spec, EntityPrototype prototype)
    {
        if (GetRepairCollisionFixtures(prototype) is not { } structure)
            return true;
        _repairDebris.Clear();
        _repairPlacementHits.Clear();
        // Loose items and unanchored frames are found by the mobile collision query.
        FindMobileRepairObstructions(grid, spec, structure, _repairPlacementHits, _repairDebris);
        var origin = _transform.ToMapCoordinates(new EntityCoordinates(grid, spec.LocalPosition));
        var construction = new PhysicsTransform(origin.Position, _transform.GetWorldRotation(grid) + spec.Rotation);
        if (!_repairGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        CollectRepairSnapshotOccupants(grid, mapGrid, tile, chunk);
        foreach (var uid in _repairTileOccupants)
        {
            // A frame deliberately present in the snapshot belongs there; only other remnants move away.
            if (_repairSnapshotOccupants.ContainsKey(uid) || !IsMovableRepairDebris(uid) ||
                !_repairBodyQuery.TryGetComponent(uid, out var body) || !body.CanCollide ||
                !_repairFixturesQuery.TryGetComponent(uid, out var fixtures))
                continue;
            foreach (var fixture in structure.Fixtures.Values)
            {
                if (!RepairFixtureIntersectsBody(fixture, construction, uid, fixtures))
                    continue;
                _repairDebris.Add(uid);
                break;
            }
        }
        if (_repairDebris.Count == 0)
            return true;

        _repairDebrisMoves.Clear();
        foreach (var uid in _repairDebris)
        {
            if (TerminatingOrDeleted(uid) || !IsMovableRepairDebris(uid) ||
                !_repairFixturesQuery.TryGetComponent(uid, out var fixtures))
                continue;

            var found = false;
            // Bounded nearby search only when actually constructing, never on each planning/pathfinding tick.
            for (var ring = 1; ring <= 3 && !found; ring++)
            for (var direction = 0; direction < 8; direction++)
            {
                var position = spec.LocalPosition + new Angle(direction * Math.Tau / 8).ToVec() * ring;
                var coordinates = new EntityCoordinates(grid, position);
                if (!CanPlaceRepairDebris(uid, fixtures, coordinates, structure, construction))
                    continue;
                _repairDebrisMoves.Add((uid, coordinates));
                found = true;
                break;
            }
            if (!found)
                return false;
        }

        // Find every destination before moving anything. Contents, stacks and other item state are retained.
        foreach (var (uid, position) in _repairDebrisMoves)
        {
            if (TerminatingOrDeleted(uid) || !IsMovableRepairDebris(uid))
                continue;
            var rotation = _transform.GetWorldRotation(uid);
            _transform.Unanchor(uid);
            if (TerminatingOrDeleted(uid))
                return false;
            _transform.SetCoordinates(uid, position);
            _transform.AttachToGridOrMap(uid);
            _transform.SetWorldRotation(uid, rotation);
        }
        return true;
    }

    private bool CanPlaceRepairDebris(EntityUid item, FixturesComponent fixtures, EntityCoordinates coordinates,
        FixturesComponent structure, PhysicsTransform construction)
    {
        var map = _transform.ToMapCoordinates(coordinates);
        var placement = new PhysicsTransform(map.Position, _transform.GetWorldRotation(item));
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            foreach (var future in structure.Fixtures.Values)
            {
                if (RepairFixturesOverlap(fixture, placement, future, construction))
                    return false;
            }

            _repairPlacementHits.Clear();
            _lookup.GetEntitiesIntersecting(map.MapId, fixture.Shape, placement, _repairPlacementHits,
                LookupFlags.Static | LookupFlags.Dynamic);
            foreach (var uid in _repairPlacementHits)
            {
                if (uid == item || _repairDebris.Contains(uid) || TerminatingOrDeleted(uid) ||
                    !_repairBodyQuery.TryGetComponent(uid, out var body) || !body.CanCollide ||
                    !_repairFixturesQuery.TryGetComponent(uid, out var other))
                    continue;
                if (RepairFixtureIntersectsBody(fixture, placement, uid, other))
                    return false;
            }

            foreach (var (uid, position) in _repairDebrisMoves)
            {
                if (!_repairFixturesQuery.TryGetComponent(uid, out var other))
                    continue;
                var otherMap = _transform.ToMapCoordinates(position);
                var otherPlacement = new PhysicsTransform(otherMap.Position, _transform.GetWorldRotation(uid));
                foreach (var occupied in other.Fixtures.Values)
                {
                    if (RepairFixturesOverlap(fixture, placement, occupied, otherPlacement))
                        return false;
                }
            }
        }
        return true;
    }
}
