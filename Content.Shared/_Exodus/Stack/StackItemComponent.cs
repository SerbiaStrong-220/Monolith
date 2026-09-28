using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Stack;

/// <summary>
/// A stack of individual items: entity-based crafting and refining consume one unit at a time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class StackItemComponent : Component
{
    /// <summary>
    /// Consume one unit when triggered, deleting the entity only when the stack is empty.
    /// </summary>
    [DataField]
    public bool ConsumeOnTrigger;

    /// <summary>
    /// Additional sprite layers and the minimum stack count at which each becomes visible.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> LayerThresholds = new();
}
