using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Biocode;

/// <summary>
/// Generic biocoded item/machine. Only users matching the access conditions may interact with,
/// wear, or use the entity. Conditions and reactions are fully data-driven, so this is not tied
/// to Asakim — any faction/role gate can be expressed through whitelists.
///
/// A user is considered authorized if EITHER:
/// - they pass <see cref="Whitelist"/> (checked against the user entity itself), OR
/// - they pass <see cref="MindWhitelist"/> (checked against the mind entity attached to the user).
/// </summary>
[RegisterComponent]
public sealed partial class BiocodeComponent : Component
{
    /// <summary>
    /// Condition checked against the user entity (components/tags on the puppet).
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Condition checked against the mind entity attached to the user (typically mind roles).
    /// Use this for player-bound identity gates so the check follows the player across
    /// body swaps / resleeves rather than depending on a specific organ.
    /// </summary>
    [DataField]
    public EntityWhitelist? MindWhitelist;

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
    /// Withhold all item actions this entity would grant (dash, etc.) from non-authorized users.
    /// </summary>
    [DataField]
    public bool BlockItemActions;

    /// <summary>
    /// Run the entity's trigger when a non-authorized live wearer is detected (equipped while alive,
    /// or a mind attaches to the wearer's body). The actual reaction (gib, explosion, etc.) is
    /// defined by BiocodeRejectedEvent handlers or trigger behaviors on the prototype.
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
