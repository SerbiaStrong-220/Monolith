using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Medical;

/// <summary>Renews manual input or selects an automatic target. A null target stops treatment.</summary>
[Serializable, NetSerializable]
public sealed class MedicalBeamGunInputEvent(NetEntity gun, NetEntity? target, MedicalBeamMode mode) : EntityEventArgs
{
    public NetEntity Gun { get; } = gun;
    public NetEntity? Target { get; } = target;
    public MedicalBeamMode Mode { get; } = mode;
}
