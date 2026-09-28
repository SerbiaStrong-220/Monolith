using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Transient grid-local index maintained by the engine's component tree.</summary>
[RegisterComponent]
public sealed partial class RotColonySiteTreeComponent : Component, IComponentTreeComponent<RotColonySiteComponent>
{
    public DynamicTree<ComponentTreeEntry<RotColonySiteComponent>> Tree { get; set; } = default!;
}
