using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.MedicalTracking;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MedicalTrackingPinpointerComponent : Component
{
    /// <summary>Interval between registered brain list refreshes and target validity checks.</summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>Next refresh, aligned to the shared interval boundaries.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;
}
