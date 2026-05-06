using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Server-side nebula lookup. Reads authoritative shape data straight off the map's
/// <see cref="NebulaMapComponent"/>.
/// </summary>
public sealed class NebulaSystem : SharedNebulaSystem
{
    protected override bool TryGetMapData(
        MapId mapId,
        out IReadOnlyList<NebulaShape> shapes,
        out IReadOnlyList<NebulaType> types)
    {
        shapes = Array.Empty<NebulaShape>();
        types = Array.Empty<NebulaType>();

        if (!MapManager.MapExists(mapId))
            return false;

        var mapUid = MapManager.GetMapEntityId(mapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var component) || component.Nebulas.Count == 0)
            return false;

        shapes = component.Nebulas;
        types = component.NebulaTypes;
        return true;
    }
}
