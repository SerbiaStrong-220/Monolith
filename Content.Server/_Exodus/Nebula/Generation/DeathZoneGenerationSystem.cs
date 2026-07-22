using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Mono.Radar;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Prototypes;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula.Generation;

/// <summary>
/// Generates the world-end death zone ring after the station-generation pass.
/// All radii, marker prototype ids and radar parameters come from
/// <see cref="NebulaGenerationConfigPrototype"/>.
/// Spawns two marker entities (inner / outer concentric sub-zones split at
/// <see cref="NebulaGenerationConfigPrototype.WorldEndMidRadius"/>); only the inner marker
/// carries a radar blip — the mid-radius boundary is intentionally invisible to players.
/// </summary>
public sealed partial class DeathZoneGenerationSystem : EntitySystem
{
    private const int RadarContourSamples = 512;
    private const float WorldEndRadarOuterFillRadius = 500000f;

    [ValidatePrototypeId<NebulaGenerationConfigPrototype>]
    private const string DefaultConfigId = "Default";

    // Fallback when the marker prototype has no NebulaRadarVisualsComponent.
    // Real colour should always come from the marker's YAML radarColor field.
    private static readonly Color FallbackRadarColor = new(1f, 0.1f, 0f, 1f);

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    public bool Generate(MapId mapId)
    {
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        var config = ResolveConfig();
        var mapComponent = EnsureComp<NebulaMapComponent>(mapUid.Value);

        var seed = _random.Next();
        mapComponent.WorldEnd = WorldEndNebulaShape.Generate(
            seed,
            config.WorldEndInnerRadius,
            config.WorldEndMidRadius,
            samples: config.SampleCount,
            minWaveAmplitude: config.WorldEndMinWaveAmplitude,
            maxWaveAmplitude: config.WorldEndMaxWaveAmplitude,
            clearanceMultiplier: config.WorldEndClearanceMultiplier,
            waveFrequencies: config.WorldEndWaveFrequencies);
        mapComponent.WorldEndInnerMarker = config.DeathZoneInnerMarker;
        mapComponent.WorldEndOuterMarker = config.DeathZoneOuterMarker;

        mapComponent.WorldEndRadarColor = SpawnDeathZoneMarkers(mapId, mapComponent.WorldEnd, config);

        var data = EnsureComp<NebulaMapDataComponent>(mapUid.Value);
        data.WorldEnd = mapComponent.WorldEnd;
        data.WorldEndInnerMarker = mapComponent.WorldEndInnerMarker;
        data.WorldEndOuterMarker = mapComponent.WorldEndOuterMarker;
        AddOrReplaceWorldEndRadarBlip(data, mapComponent.WorldEnd, mapComponent.WorldEndRadarColor);
        Dirty(mapUid.Value, data);

        _sawmill.Info($"Generated world-end death zone: inner radius {config.WorldEndInnerRadius}, mid radius {config.WorldEndMidRadius}, outer radius {mapComponent.WorldEnd.OuterBoundingRadius:0}, seed {seed}.");

        return true;
    }

    /// <summary>
    /// Same fallback pattern as <c>NebulaGenerationSystem.ResolveConfig</c>; the prototype is
    /// resolved once each round by the explicit round-start generation coordinator.
    /// </summary>
    private NebulaGenerationConfigPrototype ResolveConfig()
    {
        if (_prototype.TryIndex<NebulaGenerationConfigPrototype>(DefaultConfigId, out var config))
            return config;

        _sawmill.Warning($"Nebula generation config '{DefaultConfigId}' not found; using hardcoded fallback.");
        return new NebulaGenerationConfigPrototype();
    }

    private Color SpawnDeathZoneMarkers(MapId mapId, WorldEndNebulaShape worldEnd, NebulaGenerationConfigPrototype config)
    {
        var color = SpawnDeathZoneMarker(mapId, worldEnd, config, config.DeathZoneInnerMarker, withRadarBlip: true);
        SpawnDeathZoneMarker(mapId, worldEnd, config, config.DeathZoneOuterMarker, withRadarBlip: false);
        return color ?? FallbackRadarColor;
    }

    private Color? SpawnDeathZoneMarker(MapId mapId, WorldEndNebulaShape worldEnd, NebulaGenerationConfigPrototype config, EntProtoId prototype, bool withRadarBlip)
    {
        var marker = Spawn(prototype, new MapCoordinates(Vector2.Zero, mapId));

        var nebulaComp = EnsureComp<NebulaComponent>(marker);
        nebulaComp.Index = -1;

        if (!withRadarBlip)
        {
            // The marker inherits RadarBlipComponent from NebulaBaseMarker. Without a tuned
            // BlipConfig it would render as a small default circle at the world origin on any
            // radar that doesn't filter by NebulaPolygon shape. Strip it for the outer marker.
            RemComp<RadarBlipComponent>(marker);
            return null;
        }

        var color = TryComp<NebulaRadarVisualsComponent>(marker, out var visuals)
            ? visuals.RadarColor
            : FallbackRadarColor;

        var blip = EnsureComp<RadarBlipComponent>(marker);
        blip.MaxDistance = config.RadarMaxDistance;
        blip.RequireNoGrid = true;
        blip.VisibleFromOtherGrids = true;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(
                -worldEnd.InnerBoundingRadius, -worldEnd.InnerBoundingRadius,
                worldEnd.InnerBoundingRadius, worldEnd.InnerBoundingRadius),
            Color = color,
            Shape = RadarBlipShape.NebulaPolygon,
            Points = BuildBoundaryPoints(worldEnd),
            InvertFill = true,
            OuterFillRadius = WorldEndRadarOuterFillRadius,
            RespectZoom = true,
            Rotate = false,
        };

        return color;
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

    private static void AddOrReplaceWorldEndRadarBlip(NebulaMapDataComponent data, WorldEndNebulaShape worldEnd, Color color)
    {
        for (var i = data.RadarBlips.Count - 1; i >= 0; i--)
        {
            var config = data.RadarBlips[i].Config;
            if (config.Shape == RadarBlipShape.NebulaPolygon && config.InvertFill)
                data.RadarBlips.RemoveAt(i);
        }

        data.RadarBlips.Add(new NebulaRadarBlipSummary(
            Vector2.Zero,
            new BlipConfig
            {
                Bounds = new Box2(
                    -worldEnd.InnerBoundingRadius, -worldEnd.InnerBoundingRadius,
                    worldEnd.InnerBoundingRadius, worldEnd.InnerBoundingRadius),
                Color = color,
                Shape = RadarBlipShape.NebulaPolygon,
                Points = BuildBoundaryPoints(worldEnd),
                InvertFill = true,
                OuterFillRadius = WorldEndRadarOuterFillRadius,
                RespectZoom = true,
                Rotate = false,
            }));
    }
}
