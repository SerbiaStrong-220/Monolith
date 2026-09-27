using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.MedicalTracking;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MedicalTrackingTabletComponent : Component
{
    /// <summary>Refreshes open interfaces without resampling implant positions.</summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>Next client-list refresh, aligned to the shared interval boundaries.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;
}
