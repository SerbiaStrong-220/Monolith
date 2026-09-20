using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    [Dependency] private IPrototypeManager _repairPrototypes = default!;

    private bool OrderClosureWork(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairClosureCheck check)
    {
        var work = check.Plan.Work;
        if (work.Count == 1)
        {
            check.Order.Add(work[0]);
            return true;
        }

        // If A would hide B from this work position, finish B before creating A. Checking all pairs
        // once avoids trial spawning and preserves the existing floor order among independent jobs.
        var dependencies = new int[work.Count];
        var dependants = new List<int>?[work.Count];
        var added = new bool[work.Count];
        for (var i = 0; i < work.Count; i++)
        {
            var blocksRay = false;
            foreach (var shape in check.Shapes)
                blocksRay |= shape.Work.Target == work[i].Target && shape.BlocksRay;
            if (!blocksRay)
                continue;
            for (var j = 0; j < work.Count; j++)
            {
                if (i == j || CanReachWork(ent, grid, work[j], closure: check, onlyClosureObstacle: work[i].Target))
                    continue;
                dependencies[i]++;
                (dependants[j] ??= new List<int>()).Add(i);
            }
        }
        while (check.Order.Count < work.Count)
        {
            var next = -1;
            for (var i = 0; i < work.Count; i++)
            {
                if (!added[i] && dependencies[i] == 0)
                {
                    next = i;
                    break;
                }
            }
            if (next < 0)
                return false;
            added[next] = true;
            check.Order.Add(work[next]);
            if (dependants[next] is { } list)
            {
                foreach (var index in list)
                    dependencies[index]--;
            }
        }
        return true;
    }

    private void CollectClosureShapes(Entity<ShipRepairDroneComponent> ent, ShipRepairClosureCheck check)
    {
        foreach (var work in check.Plan.Work)
        {
            if (_repair.GetRepairCollisionFixtures(work) is not { } fixtures)
                continue;
            var passage = false;
            if (work.Prototype is { } id && _repairPrototypes.TryIndex(id, out var prototype) &&
                prototype.TryGetComponent<DoorComponent>(out var door, EntityManager.ComponentFactory) &&
                _dronePryingQuery.TryGetComponent(ent, out var prying) && prying.Enabled && prying.PryPowered &&
                door.CanPry && door.State != DoorState.Welded &&
                (!prototype.TryGetComponent<DoorBoltComponent>(out var bolts, EntityManager.ComponentFactory) || !bolts.BoltsDown || prying.Force))
                passage = true;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;
                var movement = !passage && ((fixture.CollisionLayer & (int) CollisionGroup.FlyingMobMask) != 0 ||
                    (fixture.CollisionMask & (int) CollisionGroup.FlyingMobLayer) != 0);
                // An openable door preserves navigation, but initially still blocks the repair beam.
                var ray = (fixture.CollisionLayer & (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable)) != 0;
                if (movement || ray)
                    check.Shapes.Add(new ShipRepairClosureShape(work, fixture.Shape, movement, ray));
            }
        }
    }

    private static bool ClosureIntersects(ShipRepairClosureCheck check, Vector2 from, Vector2 to, float radius,
        ShipRepairWork? target = null, ShipRepairTarget? onlyObstacle = null, bool rayOnly = false)
    {
        foreach (var obstacle in check.Shapes)
        {
            if (onlyObstacle != null && obstacle.Work.Target != onlyObstacle)
                continue;
            if (target == null && !rayOnly ? !obstacle.BlocksMovement : !obstacle.BlocksRay)
                continue;
            // Match the normal snapshot-neighbour exemption for deliberately stacked structures.
            if (target != null && (obstacle.Work.Target == target.Target ||
                target.WallMountArc == null && target.Target.EntityId != null && obstacle.Work.Target.Tile == target.Target.Tile))
                continue;
            var rotation = -obstacle.Work.Rotation;
            var start = rotation.RotateVec(from - obstacle.Work.Position);
            var end = rotation.RotateVec(to - obstacle.Work.Position);
            if (RepairShapeIntersects(obstacle.Shape, start, end, radius))
                return true;
        }
        return false;
    }

    /// <summary>Use future collision geometry, not a fixed distance to the sprite's center.</summary>
    private bool WorkOverlapsDrone(Entity<ShipRepairDroneComponent> ent, ShipRepairWork work, Vector2 position)
    {
        if (_repair.GetRepairCollisionFixtures(work) is not { } fixtures)
            return false;
        var local = (-work.Rotation).RotateVec(position - work.Position);
        var radius = GetNavigationShape(ent).Radius;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && ((fixture.CollisionLayer & (int) CollisionGroup.FlyingMobMask) != 0 ||
                (fixture.CollisionMask & (int) CollisionGroup.FlyingMobLayer) != 0) &&
                RepairShapeIntersects(fixture.Shape, local, local, radius))
                return true;
        }
        return false;
    }

    private static bool RepairShapeIntersects(IPhysShape shape, Vector2 start, Vector2 end, float radius)
    {
        switch (shape)
        {
            case PhysShapeCircle circle:
                var center = circle.Position;
                if (PointSegmentDistanceSquared(center, start, end) <= MathF.Pow(radius + circle.Radius, 2))
                    return true;
                break;
            case PolygonShape polygon:
                var insideStart = true;
                var insideEnd = true;
                var vertices = polygon.Vertices;
                var normals = polygon.Normals;
                for (var i = 0; i < vertices.Length; i++)
                {
                    insideStart &= Vector2.Dot(normals[i], start - vertices[i]) <= 0;
                    insideEnd &= Vector2.Dot(normals[i], end - vertices[i]) <= 0;
                    if (SegmentsWithin(start, end, vertices[i], vertices[(i + 1) % vertices.Length], radius + polygon.Radius))
                        return true;
                }
                if (insideStart || insideEnd)
                    return true;
                break;
            default:
                // Includes edge-aligned AABBs; transform the query into the object's own rotation.
                for (var child = 0; child < shape.ChildCount; child++)
                {
                    var box = shape.ComputeAABB(PhysicsTransform.Empty, child);
                    if (box.Contains(start) || box.Contains(end) ||
                        SegmentsWithin(start, end, box.BottomLeft, box.BottomRight, radius) ||
                        SegmentsWithin(start, end, box.BottomRight, box.TopRight, radius) ||
                        SegmentsWithin(start, end, box.TopRight, box.TopLeft, radius) ||
                        SegmentsWithin(start, end, box.TopLeft, box.BottomLeft, radius))
                        return true;
                }
                break;
        }
        return false;
    }

    private static float PointSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var length = delta.LengthSquared();
        var fraction = length > 0 ? Math.Clamp(Vector2.Dot(point - start, delta) / length, 0f, 1f) : 0f;
        return Vector2.DistanceSquared(point, start + delta * fraction);
    }

    private static bool SegmentsWithin(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float radius)
    {
        var ab = b - a;
        var cd = d - c;
        var ac = c - a;
        var cross = ab.X * cd.Y - ab.Y * cd.X;
        if (Math.Abs(cross) > 0.000001f)
        {
            var t = (ac.X * cd.Y - ac.Y * cd.X) / cross;
            var u = (ac.X * ab.Y - ac.Y * ab.X) / cross;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                return true;
        }
        var distance = Math.Min(Math.Min(PointSegmentDistanceSquared(a, c, d), PointSegmentDistanceSquared(b, c, d)),
            Math.Min(PointSegmentDistanceSquared(c, a, b), PointSegmentDistanceSquared(d, a, b)));
        return distance <= radius * radius;
    }
}
