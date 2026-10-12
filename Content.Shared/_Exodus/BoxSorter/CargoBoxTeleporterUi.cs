using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BoxSorter;

[Serializable, NetSerializable]
public enum CargoBoxTeleporterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CargoBoxTeleporterUiState : BoundUserInterfaceState
{
    public readonly int Channel;

    public CargoBoxTeleporterUiState(int channel)
    {
        Channel = channel;
    }
}

[Serializable, NetSerializable]
public sealed class CargoBoxTeleporterSetChannelMessage : BoundUserInterfaceMessage
{
    public readonly int Channel;

    public CargoBoxTeleporterSetChannelMessage(int channel)
    {
        Channel = channel;
    }
}
