using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Shuttles.Components;

[RegisterComponent]
public sealed partial class ShuttleEventBeaconComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Rule = string.Empty;

    [DataField]
    public bool ConsumeOnSuccess = true;

    /// <summary>
    /// Optional IFF display faction for grids summoned by this beacon.
    /// Does not change randomly spawned versions of the same event, NPC factions or ship ownership.
    /// </summary>
    [DataField]
    public ProtoId<TerritoryFactionPrototype>? ShuttleFaction;

    [DataField]
    public LocId SuccessPopup = "exodus-shuttle-event-beacon-success";

    [DataField]
    public LocId FailurePopup = "exodus-shuttle-event-beacon-failure";
}
