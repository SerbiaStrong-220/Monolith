using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

[DataDefinition]
public sealed partial class FactionAllianceOfferEntry
{
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> FirstFaction;

    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> SecondFaction;

    [DataField]
    public FactionAllianceOffer? Offer;

    [DataField]
    public TimeSpan NextOfferAtRoundTime;
}
