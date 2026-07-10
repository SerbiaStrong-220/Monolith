using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Raised on the grid entity when its territory controller changes (including to/from neutral "Unclaimed").
/// 
/// Other systems (future capture rules, alerts, IFF, scoring, etc.) can subscribe to react
/// without the core territory or banner systems knowing about them.
/// 
/// This is the primary extensibility hook for the territory control mechanic.
/// </summary>
[ByRefEvent]
public readonly record struct GridTerritoryControllerChangedEvent(
    EntityUid Grid,
    ProtoId<TerritoryFactionPrototype>? OldFaction,
    ProtoId<TerritoryFactionPrototype>? NewFaction,
    EntityUid? OldSourceBanner, // the banner entity that provided the previous claim, if any
    EntityUid? SourceBanner, // the banner entity that caused the new claim, if any
    EntityUid? Actor // the player that caused the change, if known
);
