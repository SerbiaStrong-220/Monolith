using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent, NetworkedComponent]
public sealed partial class CargoBoxTeleporterComponent : Component
{
    [DataField]
    public int Channel = 1;
}

[Serializable, NetSerializable]
public enum CargoBoxTeleporterVisuals : byte
{
    Channel,
}
