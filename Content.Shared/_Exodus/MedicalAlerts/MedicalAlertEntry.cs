using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.MedicalAlerts;

[NetSerializable, Serializable]
public readonly record struct MedicalAlertEntry(
    uint EntryId,
    MedicalAlertType AlertType,
    string SubjectName,
    ProtoId<SpeciesPrototype>? SpeciesId,
    string? GridName,
    Vector2i Position,
    TimeSpan CreatedAt);
