using System.Numerics;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private RotPlacementSystem _placementRules = default!;
    [Dependency] private RotSpreadSystem _spread = default!;

    private bool TryBuildContext(Entity<RotIntelligentComponent> ent, EntityCoordinates coordinates,
        out RotColonyStateComponent state, out Entity<MapGridComponent> grid, out Vector2i tile)
    {
        state = default!;
        grid = default;
        tile = default;
        if (!IsActiveCore(ent) || !coordinates.IsValid(EntityManager) || !float.IsFinite(coordinates.X)
            || !float.IsFinite(coordinates.Y) || !TryComp<RotColonyStateComponent>(ent, out var colony))
            return false;
        state = colony;
        if (state.Projects.Count >= ent.Comp.MaxProjects)
        {
            Feedback(ent, "rot-intelligent-project-limit");
            return false;
        }
        if (state.Grid is not { } uid || !TryComp<MapGridComponent>(uid, out var mapGrid)
            || _transform.GetGrid(coordinates) != uid || !Transform(ent).Anchored)
        {
            Feedback(ent, "rot-intelligent-no-grid");
            return false;
        }
        grid = (uid, mapGrid);
        tile = _maps.TileIndicesFor(uid, mapGrid, coordinates);
        if (!CanSee(ent, coordinates))
        {
            Feedback(ent, "rot-intelligent-not-visible");
            return false;
        }
        return true;
    }

    private bool HasConnectedNeighbor(RotColonyStateComponent state, Vector2i tile)
    {
        foreach (var offset in Neighbors)
        {
            if (state.Connected.Contains(tile + offset))
                return true;
        }
        return false;
    }

    private bool ValidateArea(Entity<RotIntelligentComponent> ent, RotColonyStateComponent state,
        Entity<MapGridComponent> grid, Vector2i origin, RotBuildingPrototype recipe, int rotation,
        EntityUid? project, out EntityUid? wall, out string reason, bool checkSupport = true)
    {
        wall = null;
        reason = "rot-intelligent-occupied";
        if (rotation is < 0 or > 3 || recipe.Size.X <= 0 || recipe.Size.Y <= 0)
            return false;
        if (recipe.Expansion && state.Cells.Count >= ent.Comp.MaxTerritory)
        {
            reason = "rot-intelligent-territory-limit";
            return false;
        }
        foreach (var tile in RotGeometry.Cells(origin, recipe.Size, rotation))
        {
            if (_turf.IsSpace(_maps.GetTileRef(grid, grid.Comp, tile)))
            {
                reason = "rot-intelligent-needs-floor";
                return false;
            }
            if (state.Reservations.TryGetValue(tile, out var reservation) && reservation != project)
                return false;
            if (_spread.IsReserved(grid, tile))
                return false;
            var point = _maps.GridTileToLocal(grid, grid.Comp, tile);
            if (!CanSee(ent, point))
            {
                reason = "rot-intelligent-not-visible";
                return false;
            }
            _placement.Clear();
            var bounds = Box2.CenteredAround(point.Position, new Vector2(0.9f));
            _lookup.GetLocalEntitiesIntersecting(grid.Owner, bounds, _placement,
                flags: LookupFlags.Uncontained);
            var hasTissue = false;
            foreach (var uid in _placement)
            {
                if (uid == project)
                    continue;
                switch (_placementRules.Classify(ent, recipe, uid))
                {
                    case RotOccupant.Clear:
                        continue;
                    case RotOccupant.OtherColony:
                        reason = "rot-intelligent-other-colony";
                        return false;
                    case RotOccupant.Tissue:
                        hasTissue = true;
                        continue;
                    case RotOccupant.ConvertibleWall when wall == null && HasComp<DamageableComponent>(uid)
                        && _destructible.DestroyedAt(uid) < FixedPoint2.MaxValue:
                        wall = uid;
                        continue;
                    default:
                        return false;
                }
            }
            if (recipe.Expansion && hasTissue)
                return false;
            if (checkSupport && !state.Connected.Contains(tile)
                && (!(recipe.Expansion || recipe.Wall && wall != null) || !HasConnectedNeighbor(state, tile)))
            {
                reason = "rot-intelligent-needs-territory";
                return false;
            }
        }
        if (recipe.RequiresExhaust)
        {
            // Thrusters face toward negative local Y; their moving tail and exhaust extend to positive Y.
            var nozzle = origin + RotGeometry.Rotate(new Vector2i(0, recipe.Size.Y), rotation);
            if (!_turf.IsSpace(_maps.GetTileRef(grid, grid.Comp, nozzle)))
            {
                reason = "rot-intelligent-exhaust-blocked";
                return false;
            }
        }
        return true;
    }
}
