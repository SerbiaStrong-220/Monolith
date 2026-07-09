using System.Numerics;
using Content.Server._Exodus.Nebula.Admin;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._Mono.Radar;
using Content.Server._NF.GameRule;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Prototypes;
using Content.Shared._Mono.Radar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula.Generation;

public sealed partial class NebulaGenerationSystem : EntitySystem
{
    private const string DebugContourPointPrototype = "NebulaDebugContourPoint";
    private const string DebugBoundingPointPrototype = "NebulaDebugBoundingPoint";
    private const string DebugProtectedPointPrototype = "NebulaDebugProtectedPoint";
    private const int NebulaRadarContourSamples = 96;
    private const int WorldEndRadarContourSamples = 512;
    private const int MaxProtectedDebugSamples = 64;
    private const float WorldEndRadarOuterFillRadius = 500000f;
    private static readonly Color FallbackRadarColor = new(0.38f, 0.70f, 1f, 0.85f);
    private static readonly Color FallbackWorldEndRadarColor = new(1f, 0.1f, 0f, 1f);

    /// <summary>
    /// Id of the <see cref="NebulaGenerationConfigPrototype"/> resolved at round start.
    /// Validate-attribute checks that the prototype exists at compile/load time.
    /// </summary>
    [ValidatePrototypeId<NebulaGenerationConfigPrototype>]
    private const string DefaultConfigId = "Default";

    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    /// <summary>Resolved at round start, kept hot for radar / marker validation.</summary>
    private NebulaGenerationConfigPrototype _config = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public bool Generate(MapId mapId)
    {
        if (!_mapManager.MapExists(mapId))
            return false;

        _config = ResolveConfig();
        var settings = BuildSettings(_config);

        var protectedStationIds = GetProtectedStationIds();
        var protectedNames = GetProtectedStationNames(protectedStationIds);
        var protectedAreas = new List<NebulaProtectedArea>();
        CollectProtectedAreas(mapId, protectedStationIds, protectedNames, protectedAreas, settings.ProtectedRadius);
        CollectProtectedPoints(protectedAreas, settings.ProtectedRadius);

        var seed = _random.Next();
        var result = NebulaGenerator.Generate(seed, protectedAreas, settings);
        var mapUid = _mapManager.GetMapEntityId(mapId);
        var component = EnsureComp<NebulaMapComponent>(mapUid);

        ClearNebulaMarkers(component);

        component.Seed = seed;
        component.Attempts = result.Attempts;
        component.MaxAttempts = result.MaxAttempts;
        component.MaxTotalArea = result.MaxTotalArea;
        component.TotalArea = result.TotalArea;
        component.Complete = result.Complete;
        component.Rejections = result.Rejections;

        component.Nebulas.Clear();
        component.Nebulas.AddRange(result.Nebulas);

        component.NebulaPrototypes.Clear();
        component.NebulaPrototypes.AddRange(result.NebulaPrototypes);

        component.ProtectedAreas.Clear();
        component.ProtectedAreas.AddRange(protectedAreas);

        SpawnNebulaMarkers(mapId, component);
        component.NextMarkerValidation = _timing.CurTime + _config.MarkerValidationInterval;

        SyncMapData(mapUid, component);

        _sawmill.Info($"Generated {component.Nebulas.Count} nebulas covering {component.TotalArea:0}/{component.MaxTotalArea:0} area and {component.NebulaMarkers.Count} markers on map {mapId} with seed {seed} after {component.Attempts}/{component.MaxAttempts} attempts.");

        return true;
    }

    private void SyncMapData(EntityUid mapUid, NebulaMapComponent source)
    {
        var data = EnsureComp<NebulaMapDataComponent>(mapUid);
        data.Nebulas.Clear();
        data.RadarBlips.Clear();

        for (var i = 0; i < source.Nebulas.Count; i++)
        {
            var marker = i < source.NebulaMarkers.Count ? source.NebulaMarkers[i] : EntityUid.Invalid;
            var summary = BuildSummary(source.Nebulas[i], GetNebulaPrototype(source, i), marker);
            data.Nebulas.Add(summary);
            data.RadarBlips.Add(BuildRadarBlip(summary.Shape, summary.RadarColor));
        }

        data.WorldEnd = source.WorldEnd;
        data.WorldEndInnerMarker = source.WorldEndInnerMarker;
        data.WorldEndOuterMarker = source.WorldEndOuterMarker;
        if (source.WorldEnd.IsGenerated)
            data.RadarBlips.Add(BuildWorldEndRadarBlip(source.WorldEnd, GetWorldEndRadarColor(source)));

        Dirty(mapUid, data);
    }

    private NebulaSummary BuildSummary(NebulaShape shape, EntProtoId prototype, EntityUid marker)
    {
        var blocksFTL = !Deleted(marker) && HasComp<NebulaFTLBlockerComponent>(marker);

        string? parallax = null;
        if (!Deleted(marker) && TryComp<NebulaParallaxComponent>(marker, out var parallaxComp))
            parallax = parallaxComp.Parallax;

        var radarColor = FallbackRadarColor;
        if (!Deleted(marker) && TryComp<NebulaRadarVisualsComponent>(marker, out var visuals))
            radarColor = visuals.RadarColor;

        return new NebulaSummary(shape, prototype, blocksFTL, parallax, radarColor);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<NebulaMapComponent, MapComponent>();
        while (query.MoveNext(out _, out var component, out var map))
        {
            if (component.Nebulas.Count == 0 || _timing.CurTime < component.NextMarkerValidation)
                continue;

            component.NextMarkerValidation = _timing.CurTime + _config.MarkerValidationInterval;

            // Shape data lives on the map component; marker entities are runtime radar/VV handles and can be restored.
            // CleanupImmune prevents normal cleanup; this extra pass repairs manual or unexpected component loss.
            var restored = EnsureValidMarkers(map.MapId, component);
            if (restored == 0)
                continue;

            _sawmill.Warning($"Restored {restored} nebula markers/components on map {map.MapId}.");
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<NebulaMapComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            ClearNebulaMarkers(component);
            RemCompDeferred<NebulaMapComponent>(uid);
            RemCompDeferred<NebulaMapDataComponent>(uid);
        }

        var markerQuery = EntityQueryEnumerator<NebulaComponent>();
        while (markerQuery.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }
    }

    /// <summary>
    /// Resolves the active generation config prototype. Falls back to safe hardcoded defaults
    /// (a fresh <see cref="NebulaGenerationConfigPrototype"/>) when the YAML entry is missing,
    /// so the round still starts even if content is broken.
    /// </summary>
    private NebulaGenerationConfigPrototype ResolveConfig()
    {
        if (_prototype.TryIndex<NebulaGenerationConfigPrototype>(DefaultConfigId, out var config))
            return config;

        _sawmill.Warning($"Nebula generation config '{DefaultConfigId}' not found; using hardcoded fallback.");
        return new NebulaGenerationConfigPrototype();
    }

    /// <summary>
    /// Projects a config prototype into the pure-math <see cref="NebulaGenerationSettings"/>
    /// container that <see cref="NebulaGenerator"/> consumes. Keeps the generator decoupled
    /// from <c>IPrototypeManager</c>.
    /// </summary>
    private static NebulaGenerationSettings BuildSettings(NebulaGenerationConfigPrototype config)
    {
        var pool = new (EntProtoId Proto, float Weight)[config.Markers.Count];
        for (var i = 0; i < config.Markers.Count; i++)
            pool[i] = (config.Markers[i].Proto, config.Markers[i].Weight);

        return new NebulaGenerationSettings
        {
            MaxTotalAreaOptions = new[] { config.MaxTotalArea },
            MaxAttempts = config.MaxAttempts,
            SampleCount = config.SampleCount,
            MinArea = config.MinArea,
            MaxArea = config.MaxArea,
            CoordinateLimit = config.CoordinateLimit,
            ProtectedRadius = config.ProtectedRadius,
            Separation = config.Separation,
            MinStretch = config.MinStretch,
            MaxStretch = config.MaxStretch,
            MinPower = config.MinPower,
            MaxPower = config.MaxPower,
            MinWaveAmplitude = config.MinWaveAmplitude,
            MaxWaveAmplitude = config.MaxWaveAmplitude,
            MinWaveFrequency = config.MinWaveFrequency,
            MaxWaveFrequency = config.MaxWaveFrequency,
            NebulaPrototypePool = pool,
        };
    }

    private HashSet<string> GetProtectedStationIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var proto in _prototype.EnumeratePrototypes<NebulaProtectedStationPrototype>())
            ids.Add(proto.ID);

        return ids;
    }

    private HashSet<string> GetProtectedStationNames(HashSet<string> protectedStationIds)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in protectedStationIds)
        {
            if (_prototype.TryIndex<PointOfInterestPrototype>(id, out var poi))
                names.Add(poi.Name);

            if (_prototype.TryIndex<GameMapPrototype>(id, out var map))
                names.Add(map.MapName);
        }

        return names;
    }

    private void CollectProtectedAreas(MapId mapId, HashSet<string> protectedStationIds, HashSet<string> protectedNames, List<NebulaProtectedArea> protectedAreas, float protectedRadius)
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

                if (!stationNameProtected && !IsProtectedGrid(gridUid, protectedStationIds, protectedNames))
                    continue;

                AddProtectedArea(protectedAreas, GetProtectedArea(grid, xform, protectedRadius));
            }
        }
    }

    private void CollectProtectedPoints(List<NebulaProtectedArea> protectedAreas, float defaultRadius)
    {
        foreach (var proto in _prototype.EnumeratePrototypes<NebulaProtectedPointPrototype>())
        {
            var radius = proto.Radius > 0f ? proto.Radius : defaultRadius;
            AddProtectedArea(protectedAreas, new NebulaProtectedArea(proto.Position, radius));
        }
    }

    private bool IsProtectedGrid(EntityUid gridUid, HashSet<string> protectedStationIds, HashSet<string> protectedNames)
    {
        if (TryComp<BecomesStationComponent>(gridUid, out var becomesStation) &&
            protectedStationIds.Contains(becomesStation.Id))
        {
            return true;
        }

        return protectedNames.Contains(MetaData(gridUid).EntityName);
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

        var protectedSampleCount = Math.Min(sampleCount, MaxProtectedDebugSamples);
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

        var configId = string.IsNullOrEmpty(_config.ID) ? "<hardcoded fallback>" : _config.ID;
        var lines = new List<string>
        {
            $"Config: '{configId}' (coordinateLimit {_config.CoordinateLimit:0}, {_config.Markers.Count} marker entries).",
            $"Blob nebulas: {component.Nebulas.Count}; area: {component.TotalArea:0}/{component.MaxTotalArea:0}; markers alive: {validMarkers}/{component.NebulaMarkers.Count}; seed: {component.Seed}; attempts: {component.Attempts}/{component.MaxAttempts}; complete: {component.Complete}.",
        };

        if (component.WorldEnd.IsGenerated)
        {
            lines.Add($"Death zone: inner radius {component.WorldEnd.InnerBoundingRadius:0}, mid radius {component.WorldEnd.MidRadius:0}, outer boundary {component.WorldEnd.OuterBoundingRadius:0}.");
            lines.Add($"  Inner marker: {component.WorldEndInnerMarker}");
            lines.Add($"  Outer marker: {component.WorldEndOuterMarker}");
        }
        else
        {
            lines.Add("Death zone: NOT generated.");
        }

        message = string.Join('\n', lines);
        return true;
    }

    public bool TryGetAreaStatus(bool details, out string message)
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

        var percent = component.MaxTotalArea > 0d
            ? component.TotalArea / component.MaxTotalArea * 100d
            : 0d;

        message = $"Total nebula area: {component.TotalArea:0}; max selected area: {component.MaxTotalArea:0}; budget fill: {percent:0.##}%; nebulas: {component.Nebulas.Count}.";

        if (!details)
            return true;

        for (var i = 0; i < component.Nebulas.Count; i++)
        {
            var prototype = GetNebulaPrototype(component, i);
            message += $"\n{i + 1}. {prototype}: area {component.Nebulas[i].Area:0}; center {component.Nebulas[i].Center}; bounding radius {component.Nebulas[i].BoundingRadius:0}.";
        }

        return true;
    }

    private void SpawnNebulaMarkers(MapId mapId, NebulaMapComponent component)
    {
        for (var i = 0; i < component.Nebulas.Count; i++)
        {
            var nebula = component.Nebulas[i];
            var prototype = GetNebulaPrototype(component, i);
            var marker = Spawn(prototype, new MapCoordinates(nebula.Center, mapId));
            var nebulaComponent = EnsureComp<NebulaComponent>(marker);

            nebulaComponent.Index = i;
            nebulaComponent.Shape = nebula;
            component.NebulaMarkers.Add(marker);

            ConfigureNebulaMarker(marker, nebula);

            _metadata.SetEntityName(marker, $"Nebula Marker {i + 1} ({prototype})");
        }
    }

    private void ConfigureNebulaMarker(EntityUid marker, NebulaShape nebula)
    {
        EnsureComp<PhysicsComponent>(marker);
        EnsureComp<CleanupImmuneComponent>(marker);
        ConfigureRadarBlip(marker, EnsureComp<RadarBlipComponent>(marker), nebula);
    }

    private void ConfigureRadarBlip(EntityUid marker, RadarBlipComponent blip, NebulaShape nebula)
    {
        var radius = nebula.BoundingRadius;
        blip.MaxDistance = _config.RadarMaxDistance;
        blip.RequireNoGrid = true;
        blip.VisibleFromOtherGrids = true;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(-radius, -radius, radius, radius),
            Color = TryComp<NebulaRadarVisualsComponent>(marker, out var visuals) ? visuals.RadarColor : FallbackRadarColor,
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

    private static NebulaRadarBlipSummary BuildRadarBlip(NebulaShape nebula, Color color)
    {
        var radius = nebula.BoundingRadius;
        return new NebulaRadarBlipSummary(
            nebula.Center,
            new BlipConfig
            {
                Bounds = new Box2(-radius, -radius, radius, radius),
                Color = color,
                Shape = RadarBlipShape.NebulaPolygon,
                Points = BuildRadarContourPoints(nebula),
                RespectZoom = true,
                Rotate = false,
            });
    }

    private static NebulaRadarBlipSummary BuildWorldEndRadarBlip(WorldEndNebulaShape worldEnd, Color color)
    {
        return new NebulaRadarBlipSummary(
            Vector2.Zero,
            new BlipConfig
            {
                Bounds = new Box2(
                    -worldEnd.InnerBoundingRadius, -worldEnd.InnerBoundingRadius,
                    worldEnd.InnerBoundingRadius, worldEnd.InnerBoundingRadius),
                Color = color,
                Shape = RadarBlipShape.NebulaPolygon,
                Points = BuildWorldEndBoundaryPoints(worldEnd),
                InvertFill = true,
                OuterFillRadius = WorldEndRadarOuterFillRadius,
                RespectZoom = true,
                Rotate = false,
            });
    }

    private static List<Vector2> BuildWorldEndBoundaryPoints(WorldEndNebulaShape worldEnd)
    {
        var points = new List<Vector2>(WorldEndRadarContourSamples);
        for (var i = 0; i < WorldEndRadarContourSamples; i++)
        {
            var theta = MathF.Tau * i / WorldEndRadarContourSamples;
            points.Add(worldEnd.GetBoundaryPoint(theta));
        }

        return points;
    }

    private static Color GetWorldEndRadarColor(NebulaMapComponent source)
    {
        return source.WorldEndRadarColor == default ? FallbackWorldEndRadarColor : source.WorldEndRadarColor;
    }

    private int EnsureValidMarkers(MapId mapId, NebulaMapComponent component)
    {
        var restored = 0;
        while (component.NebulaMarkers.Count < component.Nebulas.Count)
            component.NebulaMarkers.Add(EntityUid.Invalid);

        while (component.NebulaPrototypes.Count < component.Nebulas.Count)
            component.NebulaPrototypes.Add(_config.FallbackMarker);

        for (var i = 0; i < component.NebulaMarkers.Count; i++)
        {
            if (i >= component.Nebulas.Count)
                break;

            var marker = component.NebulaMarkers[i];
            var nebula = component.Nebulas[i];
            var prototype = GetNebulaPrototype(component, i);

            if (!Deleted(marker) && HasComp<NebulaComponent>(marker))
            {
                var nebulaComponent = Comp<NebulaComponent>(marker);
                nebulaComponent.Index = i;
                nebulaComponent.Shape = nebula;
                ConfigureNebulaMarker(marker, nebula);
                continue;
            }

            marker = Spawn(prototype, new MapCoordinates(nebula.Center, mapId));
            var newNebulaComponent = EnsureComp<NebulaComponent>(marker);
            newNebulaComponent.Index = i;
            newNebulaComponent.Shape = nebula;
            component.NebulaMarkers[i] = marker;

            ConfigureNebulaMarker(marker, nebula);
            _metadata.SetEntityName(marker, $"Nebula Marker {i + 1} ({prototype})");
            restored++;
        }

        if (component.NebulaMarkers.Count > component.Nebulas.Count)
        {
            for (var i = component.Nebulas.Count; i < component.NebulaMarkers.Count; i++)
            {
                var extra = component.NebulaMarkers[i];
                if (!Deleted(extra))
                    QueueDel(extra);
            }

            component.NebulaMarkers.RemoveRange(component.Nebulas.Count, component.NebulaMarkers.Count - component.Nebulas.Count);
            restored++;
        }

        if (component.NebulaPrototypes.Count > component.Nebulas.Count)
            component.NebulaPrototypes.RemoveRange(component.Nebulas.Count, component.NebulaPrototypes.Count - component.Nebulas.Count);

        return restored;
    }

    private EntProtoId GetNebulaPrototype(NebulaMapComponent component, int index)
    {
        if (index < 0 || index >= component.NebulaPrototypes.Count)
            return _config.FallbackMarker;

        var proto = component.NebulaPrototypes[index];
        return string.IsNullOrEmpty(proto.Id) ? _config.FallbackMarker : proto;
    }

    private bool IsValidMarker(EntityUid uid)
    {
        return !Deleted(uid) &&
               HasComp<NebulaComponent>(uid) &&
               HasComp<RadarBlipComponent>(uid) &&
               HasComp<PhysicsComponent>(uid) &&
               HasComp<CleanupImmuneComponent>(uid);
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
