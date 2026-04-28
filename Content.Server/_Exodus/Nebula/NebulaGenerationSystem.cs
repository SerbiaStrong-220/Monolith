using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Server._NF.GameRule;
using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared._Exodus.Nebula;
using Content.Shared._Mono.Radar;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaGenerationSystem : EntitySystem
{
    private const string DebugContourPointPrototype = "NebulaDebugContourPoint";
    private const string DebugBoundingPointPrototype = "NebulaDebugBoundingPoint";
    private const string DebugProtectedPointPrototype = "NebulaDebugProtectedPoint";
    private const float NebulaRadarMaxDistance = 250_000f;
    private const int NebulaRadarContourSamples = 96;
    private static readonly TimeSpan MarkerValidationInterval = TimeSpan.FromSeconds(30);

    private static readonly Color NebulaRadarColor = new(0.38f, 0.70f, 1f, 0.85f);

    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly string[] ProtectedStationIds =
    [
        "Frontier",
        "Medical",
        "TSFMCHalcyon",
        "TFMCHalcyon",
        "HeliosFortress",
        "PDV Helios Fortress",
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        var mapId = _ticker.DefaultMap;
        if (!_mapManager.MapExists(mapId))
            return;

        var settings = new NebulaGenerationSettings();
        var protectedNames = GetProtectedStationNames();
        var protectedAreas = new List<NebulaProtectedArea>();
        CollectProtectedAreas(mapId, protectedNames, protectedAreas, settings.ProtectedRadius);
        AddProtectedArea(protectedAreas, new NebulaProtectedArea(Vector2.Zero, settings.ProtectedRadius));

        var seed = _random.Next();
        var result = NebulaGenerator.Generate(seed, protectedAreas, settings);
        var mapUid = _mapManager.GetMapEntityId(mapId);
        var component = EnsureComp<NebulaMapComponent>(mapUid);

        ClearNebulaMarkers(component);

        component.Seed = seed;
        component.Attempts = result.Attempts;
        component.RequestedCount = result.RequestedCount;
        component.Complete = result.Complete;
        component.Rejections = result.Rejections;

        component.Nebulas.Clear();
        component.Nebulas.AddRange(result.Nebulas);

        component.ProtectedAreas.Clear();
        component.ProtectedAreas.AddRange(protectedAreas);

        SpawnNebulaMarkers(mapId, component);
        component.NextMarkerValidation = _timing.CurTime + MarkerValidationInterval;

        Logger.InfoS("nebula", $"Generated {component.Nebulas.Count}/{component.RequestedCount} nebulas and {component.NebulaMarkers.Count} markers on map {mapId} with seed {seed} after {component.Attempts} attempts.");
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<NebulaMapComponent, MapComponent>();
        while (query.MoveNext(out var uid, out var component, out var map))
        {
            if (component.Nebulas.Count == 0 || _timing.CurTime < component.NextMarkerValidation)
                continue;

            component.NextMarkerValidation = _timing.CurTime + MarkerValidationInterval;

            if (HasValidMarkers(component))
                continue;

            ClearNebulaMarkers(component);
            SpawnNebulaMarkers(map.MapId, component);

            Logger.WarningS("nebula", $"Restored {component.NebulaMarkers.Count} nebula markers on map {map.MapId}.");
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<NebulaMapComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            ClearNebulaMarkers(component);
            RemCompDeferred<NebulaMapComponent>(uid);
        }

        var markerQuery = EntityQueryEnumerator<NebulaComponent>();
        while (markerQuery.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }
    }

    private HashSet<string> GetProtectedStationNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < ProtectedStationIds.Length; i++)
        {
            var id = ProtectedStationIds[i];

            if (_prototype.TryIndex<PointOfInterestPrototype>(id, out var poi))
                names.Add(poi.Name);

            if (_prototype.TryIndex<GameMapPrototype>(id, out var map))
                names.Add(map.MapName);
        }

        return names;
    }

    private void CollectProtectedAreas(MapId mapId, HashSet<string> protectedNames, List<NebulaProtectedArea> protectedAreas, float protectedRadius)
    {
        var stationQuery = EntityQueryEnumerator<StationDataComponent>();
        while (stationQuery.MoveNext(out var stationUid, out var station))
        {
            var stationName = MetaData(stationUid).EntityName;
            var stationNameProtected = protectedNames.Contains(stationName);

            foreach (var gridUid in station.Grids)
            {
                if (!TryComp<MapGridComponent>(gridUid, out var grid) ||
                    !TryComp(gridUid, out TransformComponent? xform) ||
                    xform.MapID != mapId)
                {
                    continue;
                }

                if (!stationNameProtected && !IsProtectedGrid(gridUid, protectedNames))
                    continue;

                AddProtectedArea(protectedAreas, GetProtectedArea(grid, xform, protectedRadius));
            }
        }
    }

    private bool IsProtectedGrid(EntityUid gridUid, HashSet<string> protectedNames)
    {
        if (TryComp<BecomesStationComponent>(gridUid, out var becomesStation) &&
            IsProtectedStationId(becomesStation.Id))
        {
            return true;
        }

        return protectedNames.Contains(MetaData(gridUid).EntityName);
    }

    private static bool IsProtectedStationId(string id)
    {
        for (var i = 0; i < ProtectedStationIds.Length; i++)
        {
            if (id == ProtectedStationIds[i])
                return true;
        }

        return false;
    }

    private NebulaProtectedArea GetProtectedArea(MapGridComponent grid, TransformComponent xform, float protectedRadius)
    {
        var (worldPosition, worldRotation) = _transform.GetWorldPositionRotation(xform);
        var localCenter = grid.LocalAABB.Center;
        var worldCenter = worldPosition + worldRotation.RotateVec(localCenter);
        var gridRadius = grid.LocalAABB.Size.Length() / 2f;

        return new NebulaProtectedArea(worldCenter, gridRadius + protectedRadius);
    }

    private static void AddProtectedArea(List<NebulaProtectedArea> protectedAreas, NebulaProtectedArea area)
    {
        for (var i = 0; i < protectedAreas.Count; i++)
        {
            var existing = protectedAreas[i];
            var distance = Vector2.Distance(existing.Position, area.Position);

            if (distance + area.Radius <= existing.Radius)
                return;
        }

        protectedAreas.Add(area);
    }

    public bool TrySpawnDebugVisualization(int? nebulaIndex, int sampleCount, float lifetime, out int count, out string message)
    {
        count = 0;
        message = string.Empty;

        var mapId = _ticker.DefaultMap;
        if (!_mapManager.MapExists(mapId))
        {
            message = $"Default map {mapId} does not exist.";
            return false;
        }

        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var component) || component.Nebulas.Count == 0)
        {
            message = "No generated nebulas found on the default map.";
            return false;
        }

        if (nebulaIndex is { } index && (index < 0 || index >= component.Nebulas.Count))
        {
            message = $"Nebula index must be between 1 and {component.Nebulas.Count}.";
            return false;
        }

        var start = nebulaIndex ?? 0;
        var end = nebulaIndex + 1 ?? component.Nebulas.Count;

        for (var i = start; i < end; i++)
        {
            var nebula = component.Nebulas[i];
            count += SpawnNebulaDebugPoints(mapId, i, nebula, sampleCount, lifetime);
        }

        var protectedSampleCount = Math.Min(sampleCount, 64);
        for (var i = 0; i < component.ProtectedAreas.Count; i++)
        {
            count += SpawnProtectedAreaDebugPoints(mapId, component.ProtectedAreas[i], protectedSampleCount, lifetime);
        }

        return true;
    }

    public int ClearDebugVisuals()
    {
        var count = 0;
        var query = EntityQueryEnumerator<NebulaDebugVisualComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
            count++;
        }

        return count;
    }

    public bool TryGetStatus(out string message)
    {
        var mapId = _ticker.DefaultMap;
        if (!_mapManager.MapExists(mapId))
        {
            message = $"Default map {mapId} does not exist.";
            return false;
        }

        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var component))
        {
            message = "No NebulaMapComponent found on the default map.";
            return false;
        }

        var validMarkers = 0;
        for (var i = 0; i < component.NebulaMarkers.Count; i++)
        {
            if (IsValidMarker(component.NebulaMarkers[i]))
                validMarkers++;
        }

        message = $"Nebulas: {component.Nebulas.Count}/{component.RequestedCount}; markers: {validMarkers}/{component.NebulaMarkers.Count}; seed: {component.Seed}; complete: {component.Complete}.";
        return true;
    }

    private void SpawnNebulaMarkers(MapId mapId, NebulaMapComponent component)
    {
        for (var i = 0; i < component.Nebulas.Count; i++)
        {
            var nebula = component.Nebulas[i];
            var marker = Spawn(null, new MapCoordinates(nebula.Center, mapId));
            var nebulaComponent = EnsureComp<NebulaComponent>(marker);

            nebulaComponent.Index = i;
            nebulaComponent.Shape = nebula;
            component.NebulaMarkers.Add(marker);

            EnsureComp<PhysicsComponent>(marker);
            ConfigureRadarBlip(EnsureComp<RadarBlipComponent>(marker), nebula);

            _metadata.SetEntityName(marker, $"Nebula Marker {i + 1}");
        }
    }

    private static void ConfigureRadarBlip(RadarBlipComponent blip, NebulaShape nebula)
    {
        var radius = nebula.BoundingRadius;
        blip.MaxDistance = NebulaRadarMaxDistance;
        blip.RequireNoGrid = true;
        blip.VisibleFromOtherGrids = true;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(-radius, -radius, radius, radius),
            Color = NebulaRadarColor,
            Shape = RadarBlipShape.NebulaPolygon,
            Points = BuildRadarContourPoints(nebula),
            RespectZoom = true,
            Rotate = false,
        };
    }

    private static List<Vector2> BuildRadarContourPoints(NebulaShape nebula)
    {
        var points = new List<Vector2>(NebulaRadarContourSamples);

        for (var i = 0; i < NebulaRadarContourSamples; i++)
        {
            var theta = MathF.Tau * i / NebulaRadarContourSamples;
            points.Add(nebula.GetBoundaryPoint(theta) - nebula.Center);
        }

        return points;
    }

    private bool HasValidMarkers(NebulaMapComponent component)
    {
        if (component.NebulaMarkers.Count != component.Nebulas.Count)
            return false;

        for (var i = 0; i < component.NebulaMarkers.Count; i++)
        {
            if (!IsValidMarker(component.NebulaMarkers[i]))
                return false;
        }

        return true;
    }

    private bool IsValidMarker(EntityUid uid)
    {
        return !Deleted(uid) &&
               HasComp<NebulaComponent>(uid) &&
               HasComp<RadarBlipComponent>(uid) &&
               HasComp<PhysicsComponent>(uid);
    }

    private void ClearNebulaMarkers(NebulaMapComponent component)
    {
        for (var i = 0; i < component.NebulaMarkers.Count; i++)
        {
            var marker = component.NebulaMarkers[i];
            if (!Deleted(marker))
                QueueDel(marker);
        }

        component.NebulaMarkers.Clear();
    }

    private int SpawnNebulaDebugPoints(MapId mapId, int nebulaIndex, NebulaShape nebula, int sampleCount, float lifetime)
    {
        var count = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var theta = MathF.Tau * i / sampleCount;
            var contourPoint = nebula.GetBoundaryPoint(theta);
            SpawnDebugPoint(
                DebugContourPointPrototype,
                new MapCoordinates(contourPoint, mapId),
                nebulaIndex,
                "contour",
                lifetime);
            count++;

            var boundingPoint = nebula.Center + new Vector2(
                MathF.Cos(theta) * nebula.BoundingRadius,
                MathF.Sin(theta) * nebula.BoundingRadius);
            SpawnDebugPoint(
                DebugBoundingPointPrototype,
                new MapCoordinates(boundingPoint, mapId),
                nebulaIndex,
                "bounding",
                lifetime);
            count++;
        }

        return count;
    }

    private int SpawnProtectedAreaDebugPoints(MapId mapId, NebulaProtectedArea area, int sampleCount, float lifetime)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            var theta = MathF.Tau * i / sampleCount;
            var point = area.Position + new Vector2(
                MathF.Cos(theta) * area.Radius,
                MathF.Sin(theta) * area.Radius);
            SpawnDebugPoint(
                DebugProtectedPointPrototype,
                new MapCoordinates(point, mapId),
                -1,
                "protected",
                lifetime);
        }

        return sampleCount;
    }

    private void SpawnDebugPoint(string prototype, MapCoordinates coordinates, int nebulaIndex, string kind, float lifetime)
    {
        var uid = Spawn(prototype, coordinates);
        var debug = EnsureComp<NebulaDebugVisualComponent>(uid);
        debug.NebulaIndex = nebulaIndex;
        debug.Kind = kind;

        var despawn = EnsureComp<TimedDespawnComponent>(uid);
        despawn.Lifetime = lifetime;
    }
}
