using System.Numerics;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private float GetDroneBodyRadius(Entity<ShipRepairDroneComponent> ent)
    {
        var radius = 0f;
        if (_fixturesQuery.TryGetComponent(ent, out var fixtures))
        {
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;
                if (fixture.Shape is PhysShapeCircle circle)
                {
                    var position = circle.Position;
                    radius = Math.Max(radius, position.Length() + circle.Radius);
                    continue;
                }

                // Conservatively support other bodies without tying navigation to a particular prototype.
                for (var child = 0; child < fixture.Shape.ChildCount; child++)
                {
                    var bounds = fixture.Shape.ComputeAABB(PhysicsTransform.Empty, child);
                    var extent = Vector2.Max(Vector2.Abs(bounds.BottomLeft), Vector2.Abs(bounds.TopRight));
                    radius = Math.Max(radius, extent.Length());
                }
            }
        }
        return radius > 0f ? radius : Math.Max(PhysicsConstants.LinearSlop, ent.Comp.Clearance);
    }

    private PhysShapeCircle GetNavigationShape(Entity<ShipRepairDroneComponent> ent, bool recovering = false)
    {
        var bodyRadius = GetDroneBodyRadius(ent);
        if (recovering)
        {
            // The physics solver accepts contacts within 3 * LinearSlop. Do not treat that contact skin
            // as an enclosing wall when moving away. The actual fixture remains fully collidable.
            var radius = Math.Max(PhysicsConstants.LinearSlop, bodyRadius - 3f * PhysicsConstants.LinearSlop);
            var shape = ent.Comp.RecoveryShape;
            if (shape == null || shape.Radius != radius)
                ent.Comp.RecoveryShape = shape = new PhysShapeCircle(radius);
            return shape;
        }

        var clearance = Math.Max(bodyRadius, ent.Comp.Clearance);
        var preferred = ent.Comp.ClearanceShape;
        if (preferred == null || preferred.Radius != clearance)
            ent.Comp.ClearanceShape = preferred = new PhysShapeCircle(clearance);
        return preferred;
    }

    private void SetMovementTarget(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 destination,
        float range, float? maximumSpeed)
    {
        EnsureComp<ActiveNPCComponent>(ent);
        var steering = _steering.Register(ent, new EntityCoordinates(grid, destination));
        steering.DirectMove = true;
        steering.Range = range;
        steering.InRangeMaxSpeed = maximumSpeed;
        steering.Radius = GetDroneBodyRadius(ent);
    }

    private bool HasReachedClearanceDestination(Entity<ShipRepairDroneComponent> ent, TransformComponent xform)
    {
        if (!ent.Comp.Enabled || ent.Comp.ClearanceState != ShipRepairClearanceState.Moving ||
            ent.Comp.Grid is not { } grid || TerminatingOrDeleted(grid) ||
            !_xformQuery.TryGetComponent(grid, out var gridXform) || xform.MapID != gridXform.MapID)
            return false;
        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(xform)).Position;
        return Vector2.DistanceSquared(position, ent.Comp.ClearanceDestination) <=
               ent.Comp.ClearanceRecoveryRange * ent.Comp.ClearanceRecoveryRange;
    }

    /// <summary>Returns true while local recovery owns movement, independently of any repair job.</summary>
    private bool UpdateClearanceRecovery(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, TransformComponent xform)
    {
        if (ent.Comp.CanPhase || ent.Comp.Phased)
            return false;

        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(xform)).Position;
        if (ent.Comp.ClearanceState == ShipRepairClearanceState.Moving)
        {
            if (HasReachedClearanceDestination(ent, xform) && IsClear(ent, grid, position) &&
                CanJoinNavigation(ent, grid, position))
            {
                CompleteClearanceRecovery(ent);
                return true;
            }

            if (_timing.CurTime >= ent.Comp.ClearanceRecoveryDeadline)
                WaitForClearance(ent, ShipRepairNavigationIssue.RecoveryTimedOut);
            else if (!IsSegmentClear(ent, grid, position, ent.Comp.ClearanceDestination, false, out _, recovering: true))
                WaitForClearance(ent, ShipRepairNavigationIssue.RecoveryBlocked);
            else
                SetMovementTarget(ent, grid, ent.Comp.ClearanceDestination, ent.Comp.ClearanceRecoveryRange,
                    ent.Comp.RepairSpeedLimit);
            return true;
        }

        if (IsClear(ent, grid, position) &&
            (ent.Comp.ClearanceState == ShipRepairClearanceState.None || CanJoinNavigation(ent, grid, position)))
        {
            if (ent.Comp.ClearanceState != ShipRepairClearanceState.None)
                CompleteClearanceRecovery(ent);
            return false;
        }

        if (ent.Comp.ClearanceState == ShipRepairClearanceState.None)
            BeginClearanceRecovery(ent, grid, queue, position, ShipRepairNavigationIssue.InsufficientClearance);
        else if (_timing.CurTime >= ent.Comp.NextClearanceRecovery)
            TryStartClearanceRecovery(ent, grid, queue, position);
        return true;
    }

    private void BeginClearanceRecovery(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, Vector2 position, ShipRepairNavigationIssue issue)
    {
        // Release the whole job without blacklisting it: only our current position was rejected.
        CancelJob(ent);
        ent.Comp.NavigationIssue = issue;
        ent.Comp.ClearanceState = ShipRepairClearanceState.WaitingForSpace;
        ent.Comp.FailedPositions.Clear();
        if (_timing.CurTime >= ent.Comp.NextClearanceRecovery)
            TryStartClearanceRecovery(ent, grid, queue, position);
    }

    private bool TryStartClearanceRecovery(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, Vector2 position)
    {
        ent.Comp.NextClearanceRecovery = _timing.CurTime + ent.Comp.ClearanceRecoveryRetry;
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid) || ent.Comp.ClearanceRecoveryStep <= 0f ||
            ent.Comp.ClearanceRecoveryDistance <= 0f ||
            !IsWorldClear(ent, _transform.ToMapCoordinates(new EntityCoordinates(grid, position)),
                shape: GetNavigationShape(ent, recovering: true)))
            return false;

        // Reserve extra room for arrival tolerance, so stopping just short of the point still leaves clearance.
        var radius = GetNavigationShape(ent).Radius + ent.Comp.ClearanceRecoveryRange;
        var goalShape = ent.Comp.RecoveryDestinationShape;
        if (goalShape == null || goalShape.Radius != radius)
            ent.Comp.RecoveryDestinationShape = goalShape = new PhysShapeCircle(radius);

        var bounds = queue.Bounds.Enlarged(ent.Comp.ExteriorMargin);
        var rings = Math.Min(4, (int) MathF.Ceiling(ent.Comp.ClearanceRecoveryDistance / ent.Comp.ClearanceRecoveryStep));
        for (var ring = 1; ring <= rings; ring++)
        {
            var distance = Math.Min(ent.Comp.ClearanceRecoveryDistance, ring * ent.Comp.ClearanceRecoveryStep);
            for (var direction = 0; direction < 16; direction++)
            {
                var point = position + new Angle(direction * Math.Tau / 16).ToVec() * distance;
                var tile = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, point));
                if (!bounds.Contains(point) || !IsWorkPositionAvailable(ent, queue, tile, point) ||
                    !IsWorldClear(ent, _transform.ToMapCoordinates(new EntityCoordinates(grid, point)),
                        shape: goalShape) ||
                    !IsSegmentClear(ent, grid, position, point, false, out _, recovering: true) ||
                    !CanJoinNavigation(ent, grid, point) || !TryClaimWorkPosition(ent, queue, tile, point))
                    continue;

                ent.Comp.ClearanceDestination = point;
                ent.Comp.ClearanceState = ShipRepairClearanceState.Moving;
                ent.Comp.ClearanceRecoveryDeadline = _timing.CurTime + ent.Comp.ClearanceRecoveryTimeout;
                SetMovementTarget(ent, grid, point, ent.Comp.ClearanceRecoveryRange, ent.Comp.RepairSpeedLimit);
                return true;
            }
        }
        return false;
    }

    private bool CanJoinNavigation(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 position)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        var origin = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        if (IsSegmentClear(ent, grid, position, _map.TileCenterToVector(grid, mapGrid, origin), true, out _))
            return true;
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;
            var point = _map.TileCenterToVector(grid, mapGrid, origin + new Vector2i(x, y));
            if (IsSegmentClear(ent, grid, position, point, true, out _))
                return true;
        }
        return false;
    }

    private void WaitForClearance(Entity<ShipRepairDroneComponent> ent, ShipRepairNavigationIssue issue)
    {
        CancelJob(ent);
        ent.Comp.ClearanceState = ShipRepairClearanceState.WaitingForSpace;
        ent.Comp.NavigationIssue = issue;
        ent.Comp.NextClearanceRecovery = _timing.CurTime + ent.Comp.ClearanceRecoveryRetry;
    }

    private void CompleteClearanceRecovery(Entity<ShipRepairDroneComponent> ent)
    {
        CancelJob(ent);
        ent.Comp.NavigationIssue = ShipRepairNavigationIssue.None;
        ent.Comp.FailedTargets.Clear();
        ent.Comp.FailedPositions.Clear();
        ent.Comp.DeferredSearches.Clear();
        ent.Comp.FailureOrigin = null;
        ent.Comp.NextSearch = _timing.CurTime;
    }
}
