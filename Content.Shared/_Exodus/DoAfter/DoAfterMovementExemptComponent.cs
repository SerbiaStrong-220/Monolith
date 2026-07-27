using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.DoAfter;

/// <summary>
/// Allows an entity to continue do-afters that would otherwise be cancelled by its movement.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DoAfterMovementExemptComponent : Component;
