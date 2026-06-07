using Content.Shared.Mobs;

namespace Content.Shared._Exodus.MedicalAlerts;

/// <summary>
/// Raised when a medical tracking implant sends a rattle alert on the Medical radio channel.
/// </summary>
[ByRefEvent]
public readonly record struct MedicalAlertRaisedEvent(
    EntityUid Subject,
    MobState CurrentState,
    MobState PreviousState,
    int PositionX,
    int PositionY,
    EntityUid? GridUid,
    string? SpeciesName);
