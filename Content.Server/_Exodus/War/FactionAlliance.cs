using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

[DataDefinition]
public sealed partial class FactionAlliance
{
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> FirstFaction;

    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> SecondFaction;
}

[DataDefinition]
public sealed partial class FactionAllianceOffer
{
    [DataField(required: true)]
    public int Id;

    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> OfferingFaction;
}

public enum AllianceOfferResult : byte
{
    Success,
    StateUnavailable,
    RoundNotRunning,
    InvalidFaction,
    AtWar,
    AlreadyAllied,
    AlreadyPending,
    Cooldown,
    OfferUnavailable,
    NotOfferSender,
    NotOfferRecipient,
    NotAllied,
}

/// <summary>
/// Refreshes diplomacy consoles after an alliance proposal or a break.
/// </summary>
[ByRefEvent]
public readonly record struct AllianceOfferChangedEvent;
