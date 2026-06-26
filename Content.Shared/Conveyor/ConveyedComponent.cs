using Robust.Shared.GameStates;

namespace Content.Shared.Conveyor;

/// <summary>
/// Indicates this entity is currently contacting a conveyor and will subscribe to events as appropriate.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConveyedComponent : Component
{
    // TODO: Delete if pulling gets fixed.
    /// <summary>
    /// True if currently conveying.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Conveying;

    /// <summary>
    /// Conveyors currently contacting this entity. Managed by Start/End collide handlers.
    /// This allows conveyor force to be computed without iterating physics contacts.
    /// They're not ensured to exists, if entity was on conveyor and conveyor got deleted then entity might have deleted conveoyr uid.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> CurrentConveyors = new();
}
