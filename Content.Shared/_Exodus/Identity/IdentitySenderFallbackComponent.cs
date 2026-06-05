namespace Content.Shared._Exodus.Identity;

/// <summary>
/// When an actor interacts with this entity (e.g. a communications console) and no other system
/// could resolve their title (no ID card), use this localized fallback with the actor's visible
/// identity name. Generic — any device can provide a sender fallback.
/// </summary>
[RegisterComponent]
public sealed partial class IdentitySenderFallbackComponent : Component
{
    [DataField]
    public LocId Fallback = "identity-sender-fallback";
}
