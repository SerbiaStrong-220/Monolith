using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Biocode;

/// <summary>
/// Generic biocoded item/machine. Only users matching the access conditions may interact with,
/// wear, or use the entity. Conditions and reactions are fully data-driven, so this is not tied
/// to Asakim — any faction/role gate can be expressed through whitelists.
///
/// A user is considered authorized if EITHER:
/// - they pass <see cref="Whitelist"/> (checked against the user entity itself), OR
/// - they have a body organ that passes <see cref="OrganWhitelist"/>.
/// </summary>
[RegisterComponent]
public sealed partial class BiocodeComponent : Component
{
    /// <summary>
    /// Condition checked against the user entity (components/tags/mind roles on the puppet).
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Condition checked against the user's body organs. The user passes if any organ matches.
    /// Lets a gate depend on an implanted organ (e.g. a brain) rather than the puppet,
    /// so brain transplants are handled correctly.
    /// </summary>
    [DataField]
    public EntityWhitelist? OrganWhitelist;

    /// <summary>
    /// Block ranged/after-interact, use-in-hand, activate-in-world and UI-open for non-authorized users.
    /// </summary>
    [DataField]
    public bool BlockInteraction = true;

    /// <summary>
    /// Prevent non-authorized users from equipping this clothing.
    /// </summary>
    [DataField]
    public bool BlockEquip;

    /// <summary>
    /// Cancel <see cref="Content.Shared.Ninja.Systems.CheckDashEvent"/> for non-authorized users.
    /// </summary>
    [DataField]
    public bool BlockDash;

    /// <summary>
    /// Run the entity's trigger when a non-authorized live wearer is detected (equipped while alive,
    /// or a mind attaches to the wearer's body). The actual reaction (gib, explosion, etc.) is
    /// defined by trigger behaviors on the prototype.
    /// </summary>
    [DataField]
    public bool TriggerOnReject;

    /// <summary>
    /// Popup shown to a rejected user. Deduplicated within <see cref="PopupDedupeWindow"/> because a
    /// single physical action can raise several interaction events in succession.
    /// </summary>
    [DataField]
    public LocId RejectPopup = "biocode-rejected";

    [ViewVariables]
    public TimeSpan NextPopupAllowed;

    [DataField]
    public TimeSpan PopupDedupeWindow = TimeSpan.FromMilliseconds(250);
}
