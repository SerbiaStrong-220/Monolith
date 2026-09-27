using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Medical;

/// <summary>Renews a held healing input, or stops it when the target is null.</summary>
[Serializable, NetSerializable]
public sealed class MedicalBeamGunInputEvent(NetEntity gun, NetEntity? target) : EntityEventArgs
{
    public NetEntity Gun { get; } = gun;
    public NetEntity? Target { get; } = target;
}
