using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.MedicalTracking;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MedicalTrackingImplantComponent : Component
{
    /// <summary>Only a higher tier can replace an installed medical tracking implant.</summary>
    [DataField]
    public int Tier;

    /// <summary>Localized service tier shown on medical client cards.</summary>
    [DataField]
    public LocId TierName = "medical-tracking-tier-basic";

    /// <summary>Service badge color on tablet client cards, independent of the patient's medical condition.</summary>
    [DataField]
    public Color TierColor = Color.LightGray;

    /// <summary>Optional service border around the patient's medical HUD status icon.</summary>
    [DataField]
    public ProtoId<HealthIconPrototype>? HudBorder;

    /// <summary>Restricts radio notifications to this territory profile; null allows the whole sector.</summary>
    [DataField]
    public ProtoId<TerritoryProfilePrototype>? NotificationTerritory;

    /// <summary>Whether the implant supplies periodic body contacts to medical tablets.</summary>
    [DataField]
    public bool TrackBody;

    /// <summary>Whether implantation registers the brain as an independently tracked client.</summary>
    [DataField]
    public bool TrackBrain;

    /// <summary>Interval between body position and status samples.</summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    /// <summary>Next body sample time, shifted together with the implanted entity when paused.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>Body associated with this implant, cleared on extraction or body destruction.</summary>
    [DataField]
    public EntityUid? Body;

    /// <summary>Registered brain, which can outlive both the body and the implant.</summary>
    [DataField]
    public EntityUid? Brain;

    /// <summary>Last sampled world position and state; deliberately does not follow a moving grid.</summary>
    public MedicalTrackingContact? Contact;
}
