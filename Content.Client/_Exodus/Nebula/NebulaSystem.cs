using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;

namespace Content.Client._Exodus.Nebula;

/// <summary>
/// Client-side nebula lookup. Reads networked shape data from <see cref="NebulaFTLDataComponent"/>
/// on the map entity, which the server populates after generation.
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
        if (!TryComp<NebulaFTLDataComponent>(mapUid, out var component) || component.Nebulas.Count == 0)
            return false;

        shapes = component.Nebulas;
        types = component.Types;
        return true;
    }
}
