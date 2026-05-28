namespace Content.Shared._Exodus.Asakim;

[RegisterComponent]
public sealed partial class AsakimUseAccessComponent : Component
{
    [DataField]
    public LocId RejectedPopup = "asakim-use-access-rejected";

    /// <summary>
    /// One physical interaction (e.g. opening a UI) raises several events in succession
    /// (UseInHand → ActivatableUIOpenAttempt). Without dedup the user sees the reject popup
    /// twice in a row. We swallow repeat popups within this window.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPopupAllowed;

    [DataField]
    public TimeSpan PopupDedupeWindow = TimeSpan.FromMilliseconds(250);
}
