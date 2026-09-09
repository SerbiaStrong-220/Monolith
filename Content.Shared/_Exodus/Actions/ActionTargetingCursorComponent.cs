using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Actions;

/// <summary>
/// Opts an action entity into the crosshair cursor while selecting a target.
/// Add to the action prototype, not its performer. Independent of the held-item targeting indicator.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ActionTargetingCursorComponent : Component
{
}
