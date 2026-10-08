using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CargoBoxTeleporterComponent : Component
{

    [DataField, AutoNetworkedField]
    public string TeleporterId = "Teleporter";

    [DataField, AutoNetworkedField]
    public int Channel = 1;
}

[Serializable, NetSerializable]
public enum CargoBoxTeleporterVisuals : byte
{
    Channel,
}
