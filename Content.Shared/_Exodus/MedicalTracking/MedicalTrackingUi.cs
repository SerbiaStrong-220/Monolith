using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.MedicalTracking;

[Serializable, NetSerializable]
public enum MedicalTrackingUiKey : byte
{
    Key,
    Pinpointer,
}

[Serializable, NetSerializable]
public readonly record struct MedicalTrackingContact(NetEntity Body, string Name, string TierName,
    MapCoordinates Coordinates, MobState State, TimeSpan UpdatedAt, Color TierColor);

[Serializable, NetSerializable]
public readonly record struct MedicalTrackingBrain(NetEntity Entity, string Name, string TierName, Color TierColor);

[Serializable, NetSerializable]
public sealed class MedicalTrackingState(List<MedicalTrackingContact> contacts) : BoundUserInterfaceState
{
    public List<MedicalTrackingContact> Contacts { get; } = contacts;
}

[Serializable, NetSerializable]
public sealed class MedicalTrackingPinpointerState(
    List<MedicalTrackingBrain> brains,
    NetEntity? target) : BoundUserInterfaceState
{
    public List<MedicalTrackingBrain> Brains { get; } = brains;
    public NetEntity? Target { get; } = target;
}

[Serializable, NetSerializable]
public sealed class MedicalTrackingSelectMessage(NetEntity? target) : BoundUserInterfaceMessage
{
    public NetEntity? Target { get; } = target;
}
