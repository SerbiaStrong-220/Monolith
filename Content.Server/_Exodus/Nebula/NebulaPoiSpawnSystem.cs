using System.Numerics;
using Content.Server._NF.Station.Systems;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.Nebula;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Spawns POI grids from <see cref="NebulaPoiPrototype"/> definitions inside nebulas at the
/// start of the round. Waits for both <see cref="NebulaBlobGenerationDoneEvent"/> and
/// <see cref="WorldEndGenerationDoneEvent"/> so the candidate list is final before we
/// distribute POIs.
///
/// Distribution policy: prefer nebulas that don't yet hold any POI; once all are non-empty,
/// pick randomly. Per-POI duplicate rules and per-POI density / collision constraints
/// gate individual placements.
/// </summary>
public sealed class NebulaPoiSpawnSystem : EntitySystem
{
    private const int SampleAttempts = 16;

    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationRenameWarpsSystems _renameWarps = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    private bool _blobReady;
    private bool _worldEndReady;

    private List<Entity<MapGridComponent>> _gridBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NebulaBlobGenerationDoneEvent>(OnBlobReady);
        SubscribeLocalEvent<WorldEndGenerationDoneEvent>(OnWorldEndReady);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnBlobReady(ref NebulaBlobGenerationDoneEvent ev)
    {
        _blobReady = true;
        TrySpawn();
    }

    private void OnWorldEndReady(ref WorldEndGenerationDoneEvent ev)
    {
        _worldEndReady = true;
        TrySpawn();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _blobReady = false;
        _worldEndReady = false;
    }

    private void TrySpawn()
    {
        if (!_blobReady || !_worldEndReady)
            return;

        // Consume the flags so a stray re-raise doesn't double-spawn.
        _blobReady = false;
        _worldEndReady = false;

        var mapId = _ticker.DefaultMap;
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return;

        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent))
            return;

        SpawnAllPois(mapId, mapComponent);
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
        for (var copy = 0; copy < poi.MaxCount; copy++)
        {
            if (!TryPickNebula(poi, allowed, poiCountByCandidate, poiIdsByCandidate, out var candidateIndex))
            {
                _sawmill.Debug($"POI {poi.ID}: no nebula left for copy {copy + 1}/{poi.MaxCount} (duplicates disallowed).");
                break;
            }

            var candidate = candidates[candidateIndex];
            if (!TryPlaceCopy(mapId, mapComponent, poi, candidate, placedPoiPositions))
            {
                _sawmill.Debug($"POI {poi.ID}: could not place copy {copy + 1}/{poi.MaxCount} in nebula index {candidate.NebulaIndex}.");
                continue;
            }

            poiCountByCandidate[candidateIndex]++;
            poiIdsByCandidate[candidateIndex].Add(poi.ID);
            placedCount++;
        }

        if (placedCount > 0)
            _sawmill.Info($"POI {poi.ID}: placed {placedCount}/{poi.MaxCount}.");
    }

    private bool TryPickNebula(
        NebulaPoiPrototype poi,
        List<int> allowed,
        int[] poiCountByCandidate,
        HashSet<string>[] poiIdsByCandidate,
        out int candidateIndex)
    {
        candidateIndex = -1;

        // Filter by duplicate rule.
        var valid = new List<int>();
        for (var i = 0; i < allowed.Count; i++)
        {
            var idx = allowed[i];
            if (!poi.DuplicateAllowed && poiIdsByCandidate[idx].Contains(poi.ID))
                continue;

            valid.Add(idx);
        }

        if (valid.Count == 0)
            return false;

        // Prefer empty nebulas (no POI of any kind yet).
        var empty = new List<int>();
        for (var i = 0; i < valid.Count; i++)
        {
            if (poiCountByCandidate[valid[i]] == 0)
                empty.Add(valid[i]);
        }

        var pool = empty.Count > 0 ? empty : valid;
        candidateIndex = pool[_random.Next(pool.Count)];
        return true;
    }

    private bool TryPlaceCopy(
        MapId mapId,
        NebulaMapComponent mapComponent,
        NebulaPoiPrototype poi,
        PoiCandidate candidate,
        List<(Vector2 Position, float Radius)> placedPoiPositions)
    {
        var rng = new System.Random(_random.Next());

        for (var attempt = 0; attempt < SampleAttempts; attempt++)
        {
            if (!TrySamplePoint(rng, mapComponent, candidate, poi, out var point))
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

    private bool TrySamplePoint(System.Random rng, NebulaMapComponent mapComponent, PoiCandidate candidate, NebulaPoiPrototype poi, out Vector2 point)
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
