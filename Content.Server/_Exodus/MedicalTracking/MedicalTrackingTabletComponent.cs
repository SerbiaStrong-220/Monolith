using Content.Shared.Mobs;
using Robust.Shared.Audio;
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

    /// <summary>Interface opening sound, heard only by the user opening the tablet.</summary>
    [DataField]
    public SoundSpecifier? OpenSound;

    /// <summary>Interface closing sound, heard only by the user closing the tablet.</summary>
    [DataField]
    public SoundSpecifier? CloseSound;

    /// <summary>Positional alerts for worsening patient states, with range and volume set in sound parameters.</summary>
    [DataField]
    public Dictionary<MobState, SoundSpecifier> AlertSounds = new();

    /// <summary>Minimum interval between alerts; pending alerts are combined by severity.</summary>
    [DataField]
    public TimeSpan AlertCooldown = TimeSpan.FromSeconds(3);

    /// <summary>Earliest time the next patient alert may play.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextAlert;

    /// <summary>Most severe unannounced transition; transient notifications are not saved with the map.</summary>
    [ViewVariables]
    public MobState? PendingAlert;
}
