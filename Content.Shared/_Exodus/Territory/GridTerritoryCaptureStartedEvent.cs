using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>Raised on the grid once its banner starts contesting territory, before faction ownership is granted.</summary>
[ByRefEvent]
public readonly record struct GridTerritoryCaptureStartedEvent(
    EntityUid Grid,
    ProtoId<TerritoryFactionPrototype> Faction,
    EntityUid Banner,
    EntityUid? Actor,
    TimeSpan Duration);
