using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.Worldgen;
using Content.Shared._Exodus.Nebula.Prototypes;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Nebula.Generation;

public sealed partial class NebulaPoiSpawnSystem
{
    [Dependency] private readonly RelativePoiSpawnSystem _relativePoi = default!;
    [Dependency] private readonly SharedTransformSystem _relativeTransform = default!;

    private void OnSpawnRelativePoi(ref RelativePoiSpawnEvent args)
    {
        if (args.Rule.NebulaPoi is not { } id || !_prototype.TryIndex(id, out var poi))
            return;

        if (!_mapSystem.TryGetMap(args.Map, out var mapUid) ||
            !TryComp<NebulaMapComponent>(mapUid, out var nebulaMap))
        {
            Log.Error($"Relative POI {args.Rule.ID}: nebula generation is unavailable.");
            return;
        }

        var map = args.Map;
        var candidates = BuildCandidateList(nebulaMap);
        bool IsAllowed(Vector2 position)
        {
            if (!IsWithinSpawnDistanceLimit(position, poi.MaxSpawnDistanceFromCenter))
                return false;

            foreach (var candidate in candidates)
            {
                if (IsRelativeCandidateAllowed(map, nebulaMap, poi, candidate, position))
                    return true;
            }
            return false;
        }

        for (var copy = 0; copy < poi.MaxCount; copy++)
        {
            if (!TryLoadPoiGrid(map, poi, Vector2.Zero, relative: args.Rule, positionFilter: IsAllowed))
                break;
        }
    }

    private bool IsRelativeCandidateAllowed(MapId map, NebulaMapComponent nebulaMap,
        NebulaPoiPrototype poi, PoiCandidate candidate, Vector2 position)
    {
        if (!IsMarkerAllowed(poi, candidate.Marker) ||
            !poi.DuplicateAllowed && _relativePoi.HasNebulaCopy(map, poi.ID, candidate.NebulaIndex))
            return false;

        if (candidate.WorldEndZone is { } zone)
            return nebulaMap.WorldEnd.TryGetZone(position, out var actualZone) && zone == actualZone;

        if (candidate.BlobShape is not { } shape)
            return false;

        var density = shape.GetDensity(position);
        return density > 0f && density >= poi.MinDensity && density <= poi.MaxDensity;
    }
}
