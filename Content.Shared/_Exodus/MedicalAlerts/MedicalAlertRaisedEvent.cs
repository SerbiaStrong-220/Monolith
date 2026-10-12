using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.MedicalAlerts;

/// <summary>
/// Raised when a medical tracking implant sends a rattle alert on the Medical radio channel.
/// </summary>
[ByRefEvent]
public readonly record struct MedicalAlertRaisedEvent(
    EntityUid Subject,
    MedicalAlertType AlertType,
    Vector2i Position,
    EntityUid? GridUid,
    ProtoId<SpeciesPrototype>? SpeciesId);
