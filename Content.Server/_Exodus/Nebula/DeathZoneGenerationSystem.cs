using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Generates the world-end death zone ring after the nebula pass completes.
/// Writes <see cref="WorldEndNebulaShape"/> into <see cref="NebulaMapComponent"/>
/// and pushes it to the networked <see cref="NebulaMapDataComponent"/>.
/// </summary>
public sealed class DeathZoneGenerationSystem : EntitySystem
{
    private const float WorldEndInnerRadius = 75000f;
    private static readonly EntProtoId DeathZoneMarkerPrototype = "NebulaDeathZoneMarker";

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

        var data = EnsureComp<NebulaMapDataComponent>(mapUid.Value);
        data.WorldEnd = mapComponent.WorldEnd;
        data.WorldEndMarker = mapComponent.WorldEndMarker;
        Dirty(mapUid.Value, data);

        Logger.InfoS("nebula", $"Generated world-end death zone: inner radius {WorldEndInnerRadius}, outer radius {mapComponent.WorldEnd.OuterBoundingRadius:0}, seed {seed}.");
    }
}
