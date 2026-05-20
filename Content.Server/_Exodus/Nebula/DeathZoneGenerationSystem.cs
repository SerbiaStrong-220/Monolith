using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Nebula;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Generates the world-end death zone ring after the station-generation pass.
/// Writes <see cref="WorldEndNebulaShape"/> into <see cref="NebulaMapComponent"/>,
/// spawns two marker entities (one per concentric sub-zone, split at
/// <see cref="WorldEndMidRadius"/>), and pushes the shape to the networked
/// <see cref="NebulaMapDataComponent"/>. Only the inner marker carries a radar blip —
/// the mid-radius boundary is intentionally invisible to players.
/// </summary>
public sealed class DeathZoneGenerationSystem : EntitySystem
{
    private const float WorldEndInnerRadius = 75000f;
    private const float WorldEndMidRadius = 90000f;
    private const float NebulaRadarMaxDistance = 250_000f;
    private const int RadarContourSamples = 512;
    private static readonly EntProtoId InnerMarkerPrototype = "NebulaDeathZoneInnerMarker";
    private static readonly EntProtoId OuterMarkerPrototype = "NebulaDeathZoneOuterMarker";
    private static readonly Color DeathZoneRadarColor = new(1f, 0.1f, 0f, 1f);

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
    }

    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        var mapId = _ticker.DefaultMap;
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return;

        var mapComponent = EnsureComp<NebulaMapComponent>(mapUid.Value);

        var seed = _random.Next();
        mapComponent.WorldEnd = WorldEndNebulaShape.Generate(seed, WorldEndInnerRadius, WorldEndMidRadius);
        mapComponent.WorldEndInnerMarker = InnerMarkerPrototype;
        mapComponent.WorldEndOuterMarker = OuterMarkerPrototype;

        SpawnDeathZoneMarkers(mapId, mapComponent.WorldEnd);

        var data = EnsureComp<NebulaMapDataComponent>(mapUid.Value);
        data.WorldEnd = mapComponent.WorldEnd;
        data.WorldEndInnerMarker = mapComponent.WorldEndInnerMarker;
        data.WorldEndOuterMarker = mapComponent.WorldEndOuterMarker;
        Dirty(mapUid.Value, data);

        _sawmill.Info($"Generated world-end death zone: inner radius {WorldEndInnerRadius}, mid radius {WorldEndMidRadius}, outer radius {mapComponent.WorldEnd.OuterBoundingRadius:0}, seed {seed}.");

        var doneEv = new WorldEndGenerationDoneEvent();
        RaiseLocalEvent(ref doneEv);
    }

    private void SpawnDeathZoneMarkers(MapId mapId, WorldEndNebulaShape worldEnd)
    {
        SpawnDeathZoneMarker(mapId, worldEnd, InnerMarkerPrototype, withRadarBlip: true);
        SpawnDeathZoneMarker(mapId, worldEnd, OuterMarkerPrototype, withRadarBlip: false);
    }

    private void SpawnDeathZoneMarker(MapId mapId, WorldEndNebulaShape worldEnd, EntProtoId prototype, bool withRadarBlip)
    {
        var marker = Spawn(prototype, new MapCoordinates(Vector2.Zero, mapId));

        var nebulaComp = EnsureComp<NebulaComponent>(marker);
        nebulaComp.Index = -1;

        if (!withRadarBlip)
            return;

        var blip = EnsureComp<RadarBlipComponent>(marker);
        blip.MaxDistance = NebulaRadarMaxDistance;
        blip.RequireNoGrid = true;
        blip.VisibleFromOtherGrids = true;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(
                -worldEnd.InnerBoundingRadius, -worldEnd.InnerBoundingRadius,
                worldEnd.InnerBoundingRadius, worldEnd.InnerBoundingRadius),
            Color = DeathZoneRadarColor,
            Shape = RadarBlipShape.NebulaPolygon,
            Points = BuildBoundaryPoints(worldEnd),
            InvertFill = true,
            OuterFillRadius = 500000f,
            RespectZoom = true,
            Rotate = false,
        };
    }

    private static List<Vector2> BuildBoundaryPoints(WorldEndNebulaShape worldEnd)
    {
        var points = new List<Vector2>(RadarContourSamples);
        for (var i = 0; i < RadarContourSamples; i++)
        {
            var theta = MathF.Tau * i / RadarContourSamples;
            points.Add(worldEnd.GetBoundaryPoint(theta));
        }
        return points;
    }
}
