using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>A location that native rot creatures may return to.</summary>
[RegisterComponent]
public sealed partial class RotColonySiteComponent : Component, IComponentTreeEntry<RotColonySiteComponent>
{
    public EntityUid? TreeUid { get; set; }
    public DynamicTree<ComponentTreeEntry<RotColonySiteComponent>>? Tree { get; set; }
    public bool AddToTree => true;
    public bool TreeUpdateQueued { get; set; }
}
