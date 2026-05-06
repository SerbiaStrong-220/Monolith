using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Networked snapshot of nebula shapes/types for a map. Lives on the map entity and is the
/// authoritative source the client uses to determine FTL-blocking nebulas without depending
/// on radar visualization data.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NebulaFTLDataComponent : Component
{
    [AutoNetworkedField, ViewVariables]
    public List<NebulaShape> Nebulas = new();

    [AutoNetworkedField, ViewVariables]
    public List<NebulaType> Types = new();
}
