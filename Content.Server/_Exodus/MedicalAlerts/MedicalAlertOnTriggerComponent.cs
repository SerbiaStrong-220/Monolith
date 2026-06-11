namespace Content.Server._Exodus.MedicalAlerts;

/// <summary>
/// Marks an implant that should raise a <see cref="Shared._Exodus.MedicalAlerts.MedicalAlertRaisedEvent"/>
/// whenever it is triggered, feeding the MedAlert PDA log. Kept separate from the radio Rattle component.
/// </summary>
[RegisterComponent, Access(typeof(MedicalAlertSystem))]
public sealed partial class MedicalAlertOnTriggerComponent : Component
{
}
