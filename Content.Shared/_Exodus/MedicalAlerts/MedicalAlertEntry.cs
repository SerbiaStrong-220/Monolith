using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.MedicalAlerts;

[NetSerializable, Serializable]
public readonly record struct MedicalAlertEntry(
    uint EntryId,
    MedicalAlertType AlertType,
    string SubjectName,
    string? SpeciesName,
    string? GridName,
    int PositionX,
    int PositionY,
    TimeSpan Timestamp);
