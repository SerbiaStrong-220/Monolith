using Content.Shared.Actions;
using Content.Shared.Eye;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.GhostHiding;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GhostHidingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Action = "ActionToggleGhostHiding";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionUid;

    [DataField, AutoNetworkedField]
    public VisibilityFlags BaseVisibilityLayers = VisibilityFlags.Ghost;

    [DataField, AutoNetworkedField]
    public VisibilityFlags HidingVisibilityLayers = VisibilityFlags.Aghost;

    [DataField, AutoNetworkedField]
    public VisibilityFlags HidingVisibilityMask = VisibilityFlags.Normal | VisibilityFlags.Ghost | VisibilityFlags.Aghost;

    [DataField, AutoNetworkedField]
    public bool Hiding = false;

    [DataField, AutoNetworkedField]
    public LocId HiddenPopup = "ghost-hiding-hidden-popup";

    [DataField, AutoNetworkedField]
    public LocId NotHiddenPopup = "ghost-hiding-not-hidden-popup";
}

public sealed partial class ToggleGhostHidingActionEvent : InstantActionEvent;
