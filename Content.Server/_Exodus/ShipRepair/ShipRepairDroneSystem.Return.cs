using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private void UpdateReturn(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairStationComponent> station, TransformComponent xform, int budget)
    {
        if (Transform(station).GridUid is not { } grid || !_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return;
        if (ent.Comp.Grid != grid)
        {
            ClearAssignment(ent);
            ent.Comp.Command = ShipRepairDroneCommand.Return;
            ent.Comp.Grid = grid;
        }
        var queue = EnsureComp<ShipRepairWorkQueueComponent>(grid);
        queue.Drones.Add(ent);
        if (!queue.Indexed)
            queue.Bounds = mapGrid.LocalAABB;

        if (xform.MapID != Transform(station).MapID)
        {
            if (!ent.Comp.ReturnBlocked)
                FailJob(ent);
            return;
        }
        if (TryDockReturnedDrone(ent))
            return;
        if (ent.Comp.ClearDoAfter != null)
            return;
        if (UpdateNearbyDoors(ent, grid, queue) || UpdateClearanceRecovery(ent, grid, queue, xform))
            return;
        if (ent.Comp.Search == null && ent.Comp.Path.Count == 0)
        {
            if (_timing.CurTime < ent.Comp.NextSearch)
                return;
            if (!StartReturnNavigation(ent, station, grid, queue))
                FailJob(ent);
            return;
        }
        UpdateNavigation(ent, tool, grid, queue, budget);
    }

    private bool TryDockReturnedDrone(Entity<ShipRepairDroneComponent> ent)
    {
        if (!TryGetStation(ent, out var station) || !IsStationActive(station) ||
            !_containers.TryGetContainer(station, station.Comp.ContainerId, out var container))
            return false;
        var position = _transform.GetMapCoordinates(ent);
        var destination = _transform.GetMapCoordinates(station);
        if (position.MapId != destination.MapId || Vector2.DistanceSquared(position.Position, destination.Position) > 2.25f ||
            !_interaction.InRangeUnobstructed(ent.Owner, station.Owner, range: 1.5f))
            return false;
        // Also works without an SRD snapshot and without a pending repair job.
        return _containers.Insert(ent.Owner, container);
    }

    private bool StartReturnNavigation(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairStationComponent> station,
        EntityUid grid, ShipRepairWorkQueueComponent queue)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        CancelJob(ent);
        ent.Comp.ReturnBlocked = false;
        ent.Comp.NavigationDeadline = _timing.CurTime + ent.Comp.NavigationTimeout;
        ent.Comp.SearchDeadline = _timing.CurTime + ent.Comp.ExtendedSearchTimeout;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(ent)).Position;
        var basePosition = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(station)).Position;
        var center = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, basePosition));
        var start = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        var search = new ShipRepairPathSearch
        {
            Bounds = new Box2(Vector2.Min(mapGrid.LocalAABB.BottomLeft, position),
                Vector2.Max(mapGrid.LocalAABB.TopRight, position)).Enlarged(ent.Comp.ExteriorMargin),
            Revision = queue.NavigationRevision,
        };
        Vector2? nearest = null;
        var nearestDistance = float.PositiveInfinity;
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;
            var tile = center + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!IsWorkPositionAvailable(ent, queue, tile, point) || !IsClear(ent, grid, point, search) ||
                !_interaction.InRangeUnobstructed(_transform.ToMapCoordinates(new EntityCoordinates(grid, point)),
                    station.Owner, range: 1.5f))
                continue;
            search.Goals.Add(tile);
            search.Reverse.Costs[tile] = 0f;
            search.Reverse.Open.Enqueue(tile, 0f);
            var distance = Vector2.DistanceSquared(position, point);
            if (distance < nearestDistance)
            {
                nearest = point;
                nearestDistance = distance;
            }
        }
        if (nearest is not { } destination)
            return false;
        if (ent.Comp.CanPhase)
        {
            if (Vector2.Distance(position, destination) > ent.Comp.PathNodeLimit ||
                !TryClaimWorkPosition(ent, queue, _map.LocalToTile(grid, mapGrid,
                    new EntityCoordinates(grid, destination)), destination))
                return false;
            SetDirectPath(ent, position, destination);
            return true;
        }
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            var tile = start + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!IsSegmentClear(ent, grid, position, point, true, out _, search))
                continue;
            var cost = Vector2.Distance(position, point);
            search.Starts.Add(tile);
            search.Forward.Costs[tile] = cost;
            search.Forward.Open.Enqueue(tile, cost);
        }
        if (search.Starts.Count == 0)
        {
            BeginClearanceRecovery(ent, grid, queue, position, ShipRepairNavigationIssue.StartBlocked);
            return true;
        }
        ent.Comp.Search = search;
        ent.Comp.NavigationIssue = ShipRepairNavigationIssue.None;
        return true;
    }
}
