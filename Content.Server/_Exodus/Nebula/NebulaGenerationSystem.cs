using System.Numerics;
using Content.Server._NF.GameRule;
using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared._Exodus.Nebula;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaGenerationSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
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

        component.Seed = seed;
        component.Attempts = result.Attempts;
        component.RequestedCount = result.RequestedCount;
        component.Complete = result.Complete;
        component.Rejections = result.Rejections;

        component.Nebulas.Clear();
        component.Nebulas.AddRange(result.Nebulas);

        component.ProtectedAreas.Clear();
        component.ProtectedAreas.AddRange(protectedAreas);

        Logger.InfoS("nebula", $"Generated {component.Nebulas.Count}/{component.RequestedCount} nebulas on map {mapId} with seed {seed} after {component.Attempts} attempts.");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<NebulaMapComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaMapComponent>(uid);
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
}
