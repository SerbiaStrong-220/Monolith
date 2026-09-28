using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Mining.Pipes;

/// <summary>
/// Makes the local material buffer accessible through this machine's mining pipe port.
/// Contents are serialized by MaterialStorage, never by the transient node group.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MiningPipeNetworkMemberComponent : Component
{
    [DataField]
    public string Node = "pipe";

    /// <summary>Whether other machines can draw from this buffer. Receivers keep their own reserve.</summary>
    [DataField]
    public bool SupplyMaterials = true;

    /// <summary>Read-only client view of remote buffers. Never used as authoritative storage.</summary>
    [ViewVariables, AutoNetworkedField]
    public Dictionary<ProtoId<MaterialPrototype>, int> RemoteMaterials = new();

    /// <summary>Server-side notification that material, capacity or connectivity changes need a new UI snapshot.</summary>
    [ViewVariables]
    public bool ClientMaterialsDirty;
}
