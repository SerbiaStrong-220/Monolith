using Content.Shared.Whitelist;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Biocode;

/// <summary>
/// Guards organ and body-part slots on a body part: only entities matching the whitelist may be
/// inserted into the listed slots. Fully data-driven, not tied to Asakim.
///
/// - <see cref="OrganSlots"/>: the inserted organ itself is checked against <see cref="Whitelist"/>.
/// - <see cref="PartSlots"/>: the inserted body part is accepted only if it contains an organ that
///   passes <see cref="Whitelist"/> (e.g. a head is allowed only if it carries the right brain).
/// </summary>
[RegisterComponent]
public sealed partial class BodySlotInsertGuardComponent : Component
{
    /// <summary>
    /// Organ slot ids (e.g. "brain") whose inserted organ must pass the whitelist.
    /// </summary>
    [DataField]
    public List<string> OrganSlots = new();

    /// <summary>
    /// Body part slot ids (e.g. "head") whose inserted part must contain a whitelist-passing organ.
    /// </summary>
    [DataField]
    public List<string> PartSlots = new();

    /// <summary>
    /// What is allowed. Null means nothing is allowed into the guarded slots.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public LocId RejectPopup = "body-slot-insert-guard-rejected";

    /// <summary>
    /// Tick this guard was created on. The body is assembled (organs/parts inserted) on the same
    /// tick the part spawns, so insertions on this tick are the initial assembly and are not guarded.
    /// Later insertions (surgery) are.
    /// </summary>
    [ViewVariables]
    public GameTick SpawnTick;
}
