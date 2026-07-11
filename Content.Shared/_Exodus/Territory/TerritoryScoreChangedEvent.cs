using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Raised when the captured territory score for a faction changes.
/// Other systems can subscribe to this for scoring, win conditions, events, etc.
/// </summary>
[ByRefEvent]
public readonly record struct TerritoryScoreChangedEvent(
    ProtoId<TerritoryFactionPrototype> Faction,
    int OldScore,
    int NewScore
);
