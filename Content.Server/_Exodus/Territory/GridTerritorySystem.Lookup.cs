using Content.Shared._Exodus.Territory;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritorySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Finds the closest territory whose influence circle contains the given map position.
    /// </summary>
    public bool TryGetTerritoryAt(MapCoordinates coordinates, out Entity<GridTerritoryComponent> territory)
    {
        territory = default;
        var found = false;
        var bestDistanceSquared = float.MaxValue;

        var query = EntityQueryEnumerator<GridTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Radius <= 0f || xform.MapID != coordinates.MapId)
                continue;

            var distanceSquared = (_transform.GetWorldPosition(xform) - coordinates.Position).LengthSquared();
            var radiusSquared = comp.Radius * comp.Radius;
            if (distanceSquared > radiusSquared || distanceSquared >= bestDistanceSquared)
                continue;

            territory = (uid, comp);
            bestDistanceSquared = distanceSquared;
            found = true;
        }

        return found;
    }
}
