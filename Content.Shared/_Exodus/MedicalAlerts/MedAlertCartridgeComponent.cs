namespace Content.Shared._Exodus.MedicalAlerts;

[RegisterComponent]
public sealed partial class MedAlertCartridgeComponent : Component
{
    [DataField]
    public bool NotificationsEnabled = true;
}
