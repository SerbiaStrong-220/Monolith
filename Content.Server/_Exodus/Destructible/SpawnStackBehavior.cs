using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server._Exodus.Stack;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Destructible;

/// <summary>
/// Converts each unit of the destroyed entity into stack units, spawning the configured denominations directly.
/// </summary>
[Serializable, DataDefinition]
public sealed partial class SpawnStackBehavior : IThresholdBehavior
{
    [DataField(required: true)]
    public ProtoId<StackPrototype> Stack;

    /// <summary>
    /// Descending stack sizes, ending in one so that no remainder is lost.
    /// </summary>
    [DataField(required: true)]
    public List<int> Sizes = new();

    /// <summary>
    /// Number of output units per unit of the destroyed stack (or per entity without a stack).
    /// </summary>
    [DataField]
    public int AmountPerUnit = 1;

    [DataField]
    public float Offset = 0.5f;

    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        system.EntityManager.System<StackSpawnSystem>().SpawnDrops(owner, this);
    }
}
