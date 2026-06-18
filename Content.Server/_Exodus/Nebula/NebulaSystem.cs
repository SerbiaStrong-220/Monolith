using Content.Shared._Exodus.Nebula;
using Content.Shared._Exodus.Nebula.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Server-side nebula lookup. Reads authoritative summaries from the map's
/// <see cref="NebulaMapDataComponent"/>, which the generation system populates.
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
