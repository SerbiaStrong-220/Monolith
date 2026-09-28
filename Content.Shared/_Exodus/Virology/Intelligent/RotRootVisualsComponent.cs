namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>Hand-drawn poses for the mobile body and transitions to and from rooted form.</summary>
[RegisterComponent]
public sealed partial class RotRootVisualsComponent : Component
{
    [DataField] public string MobileIdle = "mobile";
    [DataField] public string MobileMoving = "crawling";
    [DataField] public string Rooting = "rooting";
    [DataField] public string Uprooting = "uprooting";
}
