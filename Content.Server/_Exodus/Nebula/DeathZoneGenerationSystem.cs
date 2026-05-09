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
/// spawns a marker entity for radar/FTL-map visualization, and pushes the shape
/// to the networked <see cref="NebulaMapDataComponent"/>.
/// </summary>
public sealed class DeathZoneGenerationSystem : EntitySystem
{
    private const float WorldEndInnerRadius = 75000f;
    private const float NebulaRadarMaxDistance = 250_000f;
    private const int RadarContourSamples = 512;
    private static readonly EntProtoId DeathZoneMarkerPrototype = "NebulaDeathZoneMarker";
    private static readonly Color DeathZoneRadarColor = new(1f, 0.1f, 0f, 1f);

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

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
        mapComponent.WorldEnd = WorldEndNebulaShape.Generate(seed, WorldEndInnerRadius);
        mapComponent.WorldEndMarker = DeathZoneMarkerPrototype;

        SpawnDeathZoneMarker(mapId, mapComponent.WorldEnd);

        var data = EnsureComp<NebulaMapDataComponent>(mapUid.Value);
        data.WorldEnd = mapComponent.WorldEnd;
        data.WorldEndMarker = mapComponent.WorldEndMarker;
        Dirty(mapUid.Value, data);

        Logger.InfoS("nebula", $"Generated world-end death zone: inner radius {WorldEndInnerRadius}, outer radius {mapComponent.WorldEnd.OuterBoundingRadius:0}, seed {seed}.");
    }

    private void SpawnDeathZoneMarker(MapId mapId, WorldEndNebulaShape worldEnd)
    {
        var marker = Spawn(DeathZoneMarkerPrototype, new MapCoordinates(Vector2.Zero, mapId));

        var nebulaComp = EnsureComp<NebulaComponent>(marker);
        nebulaComp.Index = -1;

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
