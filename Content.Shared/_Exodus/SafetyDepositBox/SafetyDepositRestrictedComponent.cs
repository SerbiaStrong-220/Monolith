using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.SafetyDepositBox;

/// <summary>
/// Excludes an item from safety deposit storage without replacing its inherited gameplay tags.
/// Apply to restricted prototypes; descendants inherit the restriction.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SafetyDepositRestrictedComponent : Component
{
}
