using Content.Shared._Exodus.Nebula;
using Content.Shared._Exodus.Nebula.Components;
using Robust.Shared.Map;

namespace Content.Client._Exodus.Nebula;

/// <summary>
/// Client-side nebula lookup. Reads networked summaries from <see cref="NebulaMapDataComponent"/>
/// on the map entity.
/// </summary>
public sealed class NebulaSystem : SharedNebulaSystem
{
    protected override bool TryGetSummaries(MapId mapId, out IReadOnlyList<NebulaSummary> summaries)
    {
        summaries = Array.Empty<NebulaSummary>();

        if (!MapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        if (!TryComp<NebulaMapDataComponent>(mapUid, out var component) || component.Nebulas.Count == 0)
            return false;

        summaries = component.Nebulas;
        return true;
    }
}
