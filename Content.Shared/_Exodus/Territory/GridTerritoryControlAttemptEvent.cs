using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>Raised before a controller change so an active priority source can retain its territory.</summary>
[ByRefEvent]
public record struct GridTerritoryControlAttemptEvent(
    ProtoId<TerritoryFactionPrototype>? Faction,
    EntityUid? Source,
    bool Cancelled = false);
