using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Spawns entities while this anchored entity is the active territory claim source.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class TerritoryCoreComponent : Component
{
    /// <summary>
    /// Entity to spawn on the nearest free matching surface of this grid.
    /// Spawned entities are independent of the core and survive loss of control.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId SpawnPrototype;

    /// <summary>
    /// Tag identifying the anchored surface entities on which spawning is allowed.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<TagPrototype> SubstrateTag;

    /// <summary>
    /// Delay before the first spawn and between subsequent successful spawns.
    /// </summary>
    [DataField]
    public TimeSpan SpawnInterval = TimeSpan.FromMinutes(7);

    /// <summary>
    /// Retry delay when no suitable unoccupied surface remains. Missed spawns are never accumulated.
    /// </summary>
    [DataField]
    public TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    [ViewVariables]
    public EntityUid? ActiveGrid;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextSpawn;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextCheck;
}
