using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Tracks the current nebula volume containing an entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NebulaPresenceComponent : Component
{
    [DataField, AutoNetworkedField, ViewVariables]
    public int NebulaIndex = -1;

    [DataField, AutoNetworkedField, ViewVariables]
    public NebulaType Type;

    [DataField, AutoNetworkedField, ViewVariables]
    public float Density;

    [DataField, AutoNetworkedField, ViewVariables]
    public float Alpha;
}
