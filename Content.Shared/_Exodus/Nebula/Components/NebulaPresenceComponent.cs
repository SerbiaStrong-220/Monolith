using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Tracks the current nebula volume containing an entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NebulaPresenceComponent : Component
{
    [DataField, AutoNetworkedField, ViewVariables]
    public int NebulaIndex = -1;

    /// <summary>
    /// Prototype id of the nebula marker for the volume this entity is in. Per-effect
    /// components on that prototype define what behavior applies.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables]
    public EntProtoId Marker;

    [DataField, AutoNetworkedField, ViewVariables]
    public float Density;

    [DataField, AutoNetworkedField, ViewVariables]
    public float Alpha;
}
