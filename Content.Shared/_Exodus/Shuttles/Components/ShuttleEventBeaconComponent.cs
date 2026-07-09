using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Shuttles.Components;

[RegisterComponent]
public sealed partial class ShuttleEventBeaconComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Rule = string.Empty;

    [DataField]
    public bool ConsumeOnSuccess = true;

    [DataField]
    public LocId SuccessPopup = "exodus-shuttle-event-beacon-success";

    [DataField]
    public LocId FailurePopup = "exodus-shuttle-event-beacon-failure";
}
