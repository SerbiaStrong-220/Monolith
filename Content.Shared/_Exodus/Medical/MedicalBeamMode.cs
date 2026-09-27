using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Medical;

[Serializable, NetSerializable]
public enum MedicalBeamMode : byte
{
    Manual,
    Automatic,
}
