using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Presence;

/// <summary>
/// Stateless helpers used by hazard systems to resolve "which nebula is at world point X" and
/// "fetch a typed component from a marker prototype". Both checks used to be duplicated 3–4
/// times across Lightning / EMP / DebrisExclusion; this is the single source of truth.
/// </summary>
public static class NebulaQueryHelper
{
    /// <summary>
    /// Identifies the nebula containing <paramref name="position"/> on the map and outputs
    /// the marker prototype id of that nebula. Nebulas do not overlap, so at most one match
    /// is possible. Returns false (marker = default) if the point is outside any nebula or
    /// outside both death-zone sub-zones.
    /// </summary>
    public static bool TryGetMarkerAt(NebulaMapComponent map, Vector2 position, out EntProtoId marker)
    {
        for (var i = 0; i < map.Nebulas.Count; i++)
        {
            if (i >= map.NebulaPrototypes.Count)
                continue;

            var nebula = map.Nebulas[i];
            var delta = position - nebula.Center;
            if (delta.LengthSquared() > nebula.BoundingRadius * nebula.BoundingRadius)
                continue;

            if (!nebula.Contains(position))
                continue;

            marker = map.NebulaPrototypes[i];
            return marker.Id != null;
        }

        if (map.WorldEnd.IsGenerated && map.WorldEnd.TryGetZone(position, out var zone))
        {
            marker = zone == WorldEndZone.Outer
                ? map.WorldEndOuterMarker
                : map.WorldEndInnerMarker;
            return marker.Id != null;
        }

        marker = default;
        return false;
    }

    /// <summary>
    /// True if the point is inside a nebula whose marker prototype matches
    /// <paramref name="marker"/>. Used by hazards that need to verify a specific tile is in
    /// the nebula they belong to (e.g. lightning strike targeting).
    /// </summary>
    public static bool IsPositionInsideMarkerNebula(NebulaMapComponent map, Vector2 position, EntProtoId marker)
    {
        return TryGetMarkerAt(map, position, out var found) && found == marker;
    }

    /// <summary>
    /// Resolves a typed component <typeparamref name="T"/> from the marker prototype's YAML.
    /// Used by hazard systems to read their static configuration from the marker, e.g.
    /// <c>NebulaLightningHazardComponent</c> for grid lightning intervals.
    /// </summary>
    public static bool TryGetMarkerComponent<T>(
        IPrototypeManager prototype,
        IComponentFactory factory,
        EntProtoId marker,
        out T component) where T : IComponent, new()
    {
        component = default!;

        if (string.IsNullOrEmpty(marker.Id))
            return false;

        if (!prototype.TryIndex<EntityPrototype>(marker, out var proto))
            return false;

        return proto.TryGetComponent<T>(out component!, factory);
    }
}
