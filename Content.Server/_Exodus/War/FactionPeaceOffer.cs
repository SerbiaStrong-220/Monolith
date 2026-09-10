using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

[DataDefinition]
public sealed partial class FactionPeaceOffer
{
    /// <summary>
    /// Unique offer number within the sector's current round.
    /// </summary>
    [DataField(required: true)]
    public int Id;

    /// <summary>
    /// Faction that may withdraw the offer; only its opponent may accept it.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> OfferingFaction;
}

[DataDefinition]
public sealed partial class FactionWarCooldown
{
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> FirstFaction;

    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> SecondFaction;

    /// <summary>
    /// Earliest round time when either faction may declare war on the other.
    /// </summary>
    [DataField]
    public TimeSpan AvailableAtRoundTime;
}

public enum PeaceOfferResult : byte
{
    Success,
    StateUnavailable,
    RoundNotRunning,
    InvalidFaction,
    NotAtWar,
    AlreadyPending,
    Cooldown,
    OfferUnavailable,
    NotOfferSender,
    NotOfferRecipient,
}

/// <summary>
/// Refreshes diplomacy consoles without reporting a change to the active wars.
/// </summary>
[ByRefEvent]
public readonly record struct PeaceOfferChangedEvent;
