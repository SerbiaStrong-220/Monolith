using Content.Shared._Exodus.MedicalAlerts;

namespace Content.Server._Exodus.MedicalAlerts;

/// <summary>
/// Stores the sector-wide medical implant alert log.
/// </summary>
[RegisterComponent]
[Access(typeof(MedicalAlertSystem))]
public sealed partial class MedicalAlertDataComponent : Component
{
    [DataField]
    public uint LastEntryId;

    [DataField]
    public List<MedicalAlertEntry> Entries = new();
}
