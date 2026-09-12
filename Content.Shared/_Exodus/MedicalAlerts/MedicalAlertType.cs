using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.MedicalAlerts;

[Serializable, NetSerializable]
public enum MedicalAlertType : byte
{
    Critical = 0,
    Death = 1,
    Revived = 2,
}
