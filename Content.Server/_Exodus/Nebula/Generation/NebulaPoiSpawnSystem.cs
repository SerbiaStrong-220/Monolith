using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._NF.Station.Systems;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Prototypes;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula.Generation;

/// <summary>
/// Spawns POI grids from <see cref="NebulaPoiPrototype"/> definitions inside nebulas at the
/// start of the round. Called by <see cref="NebulaRoundstartGenerationSystem"/> after blob
/// nebulas and the world-end zone have both been generated, so the candidate list is final
/// before we distribute POIs.
///
/// Distribution policy: pick randomly among eligible nebulas (marker in
/// <see cref="NebulaPoiPrototype.SpawnIn"/>, respecting <see cref="NebulaPoiPrototype.DuplicateAllowed"/>).
/// Per-POI density and collision constraints gate individual placements in
/// <see cref="TryPlaceCopy"/>. When placement fails in the picked nebula, up to
/// <see cref="MaxNebulaFallbacks"/> other eligible nebulas are tried with the same
/// <see cref="SampleAttempts"/> budget each.
/// </summary>
public sealed partial class NebulaPoiSpawnSystem : EntitySystem
{
    private const int SampleAttempts = 16;
    private const int MaxNebulaFallbacks = 6;

    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapLoaderSystem _map = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationRenameWarpsSystems _renameWarps = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    private List<Entity<MapGridComponent>> _gridBuffer = new();

    public bool TrySpawnAllPois(MapId mapId)
    {
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent))
            return false;

        SpawnAllPois(mapId, mapComponent);
        return true;
    }

    private void SpawnAllPois(MapId mapId, NebulaMapComponent mapComponent)
    {
        var candidates = BuildCandidateList(mapComponent);
        if (candidates.Count == 0)
        {
            _sawmill.Debug("No nebula candidates available; skipping POI spawn.");
            return;
        }

        // POI count per nebula candidate, and which POI ids are already present there.
        // Indices match positions in `candidates`.
        var poiCountByCandidate = new int[candidates.Count];
        var poiIdsByCandidate = new HashSet<string>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
            poiIdsByCandidate[i] = new HashSet<string>(StringComparer.Ordinal);

        // Positions of POIs already placed this round, used as an extra distance constraint
        // since freshly-loaded grids may not be visible to broadphase yet.
        var placedPoiPositions = new List<(Vector2 Position, float Radius)>();

        foreach (var poi in _prototype.EnumeratePrototypes<NebulaPoiPrototype>())
        {
            if (poi.MaxCount <= 0 || poi.SpawnIn.Count == 0)
                continue;

            SpawnOnePoi(mapId, mapComponent, poi, candidates, poiCountByCandidate, poiIdsByCandidate, placedPoiPositions);
        }
    }

    private void SpawnOnePoi(
        MapId mapId,
        NebulaMapComponent mapComponent,
        NebulaPoiPrototype poi,
        List<PoiCandidate> candidates,
        int[] poiCountByCandidate,
        HashSet<string>[] poiIdsByCandidate,
        List<(Vector2 Position, float Radius)> placedPoiPositions)
    {
        // Pre-filter candidates that this POI is allowed to spawn into.
        var allowed = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            if (IsMarkerAllowed(poi, candidates[i].Marker))
                allowed.Add(i);
        }

        if (allowed.Count == 0)
        {
            _sawmill.Debug($"POI {poi.ID}: no matching nebula candidates.");
            return;
        }

        var placedCount = 0;
        var triedCandidates = new HashSet<int>();

        for (var copy = 0; copy < poi.MaxCount; copy++)
        {
            triedCandidates.Clear();
            var copyPlaced = false;

            for (var nebulaAttempt = 0; nebulaAttempt <= MaxNebulaFallbacks; nebulaAttempt++)
            {
                if (!TryPickNebula(poi, allowed, poiIdsByCandidate, triedCandidates, out var candidateIndex))
                {
                    if (nebulaAttempt == 0)
                        _sawmill.Debug($"POI {poi.ID}: no nebula left for copy {copy + 1}/{poi.MaxCount} (duplicates disallowed).");
                    break;
                }

                triedCandidates.Add(candidateIndex);
                var candidate = candidates[candidateIndex];

                if (TryPlaceCopy(mapId, mapComponent, poi, candidate, placedPoiPositions))
                {
                    poiCountByCandidate[candidateIndex]++;
                    poiIdsByCandidate[candidateIndex].Add(poi.ID);
                    placedCount++;
                    copyPlaced = true;
                    break;
                }

                if (nebulaAttempt < MaxNebulaFallbacks)
                {
                    _sawmill.Debug(
                        $"POI {poi.ID}: copy {copy + 1}/{poi.MaxCount} failed in nebula index {candidate.NebulaIndex}; trying another nebula ({nebulaAttempt + 1}/{MaxNebulaFallbacks} fallbacks used).");
                }
            }

            if (!copyPlaced && triedCandidates.Count > 0)
            {
                _sawmill.Debug(
                    $"POI {poi.ID}: could not place copy {copy + 1}/{poi.MaxCount} after trying {triedCandidates.Count} nebula(s).");
            }
        }

        if (placedCount > 0)
            _sawmill.Info($"POI {poi.ID}: placed {placedCount}/{poi.MaxCount}.");
    }

    private bool TryPickNebula(
        NebulaPoiPrototype poi,
        List<int> allowed,
        HashSet<string>[] poiIdsByCandidate,
        HashSet<int> excludedCandidates,
        out int candidateIndex)
    {
        candidateIndex = -1;

        // Filter by duplicate rule and nebulas already attempted for this copy.
        var valid = new List<int>();
        for (var i = 0; i < allowed.Count; i++)
        {
            var idx = allowed[i];
            if (excludedCandidates.Contains(idx))
                continue;

            if (!poi.DuplicateAllowed && poiIdsByCandidate[idx].Contains(poi.ID))
                continue;

            valid.Add(idx);
        }

        if (valid.Count == 0)
            return false;

        candidateIndex = valid[_random.Next(valid.Count)];
        return true;
    }

    private bool TryPlaceCopy(
        MapId mapId,
        NebulaMapComponent mapComponent,
        NebulaPoiPrototype poi,
        PoiCandidate candidate,
        List<(Vector2 Position, float Radius)> placedPoiPositions)
    {
        var rng = new RobustRandom();
        rng.SetSeed(_random.Next());

        for (var attempt = 0; attempt < SampleAttempts; attempt++)
        {
            if (!TrySamplePoint(rng, mapComponent, candidate, poi, out var point))
                continue;

            if (!IsWithinSpawnDistanceLimit(point, poi.MaxSpawnDistanceFromCenter))
                continue;

            if (HasNearbyGrid(mapId, point, poi.ProtectedRadius))
                continue;

            if (HasNearbyPlacedPoi(point, poi.ProtectedRadius, placedPoiPositions))
                continue;

            if (!TryLoadPoiGrid(mapId, poi, point))
                return false;

            placedPoiPositions.Add((point, poi.ProtectedRadius));
            return true;
        }

        return false;
    }

    private static bool IsWithinSpawnDistanceLimit(Vector2 point, float? maxDistance)
    {
        if (maxDistance == null)
            return true;

        var limit = maxDistance.Value;
        if (limit < 0f)
            return false;

        return point.LengthSquared() <= limit * limit;
    }

    private bool TrySamplePoint(IRobustRandom rng, NebulaMapComponent mapComponent, PoiCandidate candidate, NebulaPoiPrototype poi, out Vector2 point)
    {
        if (candidate.WorldEndZone is { } zone)
            return mapComponent.WorldEnd.TryGetRandomPoint(rng, zone, out point);

        if (candidate.BlobShape is { } shape)
            return shape.TryGetRandomPoint(rng, poi.MinDensity, poi.MaxDensity, out point);

        point = default;
        return false;
    }

    private bool HasNearbyGrid(MapId mapId, Vector2 position, float radius)
    {
        if (radius <= 0f)
            return false;

        var size = new Vector2(radius, radius);
        _gridBuffer.Clear();
        _mapManager.FindGridsIntersecting(
            mapId,
            new Box2(position - size, position + size),
            ref _gridBuffer,
            approx: true,
            includeMap: false);

        return _gridBuffer.Count > 0;
    }

    private static bool HasNearbyPlacedPoi(Vector2 position, float radius, List<(Vector2 Position, float Radius)> placed)
    {
        for (var i = 0; i < placed.Count; i++)
        {
            var min = MathF.Max(radius, placed[i].Radius);
            if (Vector2.Distance(position, placed[i].Position) < min)
                return true;
        }

        return false;
    }

    private bool TryLoadPoiGrid(MapId mapId, NebulaPoiPrototype poi, Vector2 point)
    {
        if (!_map.TryLoadGrid(mapId, poi.Path, out var grid, offset: point, rot: _random.NextAngle()) || grid is not { } loaded)
        {
            _sawmill.Warning($"POI {poi.ID}: failed to load grid {poi.Path}.");
            return false;
        }

        var gridUid = loaded.Owner;

        if (!string.IsNullOrEmpty(poi.Name))
            _metadata.SetEntityName(gridUid, poi.Name);

        var stationUid = TryRegisterStation(gridUid, poi);

        if (poi.AddComponents.Count > 0)
            EntityManager.AddComponents(gridUid, poi.AddComponents);

        if (stationUid is { } station && poi.HideWarp)
            _renameWarps.SyncWarpPointsToStation(station, forceAdminOnly: true);

        return true;
    }

    /// <summary>
    /// Initialises the loaded grid as a <see cref="Content.Server.Station"/> if the POI carries
    /// a <see cref="NebulaPoiPrototype.StationGameMap"/> reference. Returns null for decorative
    /// POIs (no station init) — that's the default and the cheap path.
    /// </summary>
    private EntityUid? TryRegisterStation(EntityUid gridUid, NebulaPoiPrototype poi)
    {
        if (string.IsNullOrEmpty(poi.StationGameMap))
            return null;

        if (!_prototype.TryIndex<GameMapPrototype>(poi.StationGameMap, out var gameMap))
        {
            _sawmill.Warning($"POI {poi.ID}: stationGameMap '{poi.StationGameMap}' not found.");
            return null;
        }

        if (!gameMap.Stations.TryGetValue(poi.StationGameMap, out var stationConfig))
        {
            _sawmill.Warning($"POI {poi.ID}: gameMap '{poi.StationGameMap}' has no stations entry matching its own id.");
            return null;
        }

        var stationName = string.IsNullOrEmpty(poi.Name) ? gameMap.MapName : poi.Name;
        return _station.InitializeNewStation(stationConfig, new[] { gridUid }, stationName);
    }

    private static bool IsMarkerAllowed(NebulaPoiPrototype poi, EntProtoId marker)
    {
        if (marker.Id == null)
            return false;

        for (var i = 0; i < poi.SpawnIn.Count; i++)
        {
            if (poi.SpawnIn[i].Id == marker.Id)
                return true;
        }

        return false;
    }

    private static List<PoiCandidate> BuildCandidateList(NebulaMapComponent mapComponent)
    {
        var list = new List<PoiCandidate>();

        // Blob nebulas first; each entry pairs the shape with its marker prototype id.
        for (var i = 0; i < mapComponent.Nebulas.Count; i++)
        {
            if (i >= mapComponent.NebulaPrototypes.Count)
                break;

            var marker = mapComponent.NebulaPrototypes[i];
            if (marker.Id == null)
                continue;

            list.Add(new PoiCandidate(i, marker, mapComponent.Nebulas[i], null));
        }

        // Death-zone sub-zones. Negative index distinguishes them from blob entries.
        if (mapComponent.WorldEnd.IsGenerated)
        {
            if (mapComponent.WorldEndInnerMarker.Id != null)
                list.Add(new PoiCandidate(-1, mapComponent.WorldEndInnerMarker, null, WorldEndZone.Inner));

            if (mapComponent.WorldEndOuterMarker.Id != null)
                list.Add(new PoiCandidate(-2, mapComponent.WorldEndOuterMarker, null, WorldEndZone.Outer));
        }

        return list;
    }

    private readonly record struct PoiCandidate(
        int NebulaIndex,
        EntProtoId Marker,
        NebulaShape? BlobShape,
        WorldEndZone? WorldEndZone);
}
