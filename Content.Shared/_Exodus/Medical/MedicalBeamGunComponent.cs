using Content.Shared._Exodus.Visuals;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Physics;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Medical;

/// <summary>A held tool that maintains a healing beam while aimed at a patient.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MedicalBeamGunComponent : Component
{
    /// <summary>Maximum distance between the user and patient, in meters.</summary>
    [DataField]
    public float Range = 10f;

    /// <summary>Uses the same obstruction layer as ordinary laser beams.</summary>
    [DataField]
    public CollisionGroup CollisionMask = CollisionGroup.Opaque;

    /// <summary>Manual targeting or automatic tracking of a selected patient.</summary>
    [DataField, AutoNetworkedField]
    public MedicalBeamMode Mode;

    /// <summary>Interval between server-side treatment and obstruction checks.</summary>
    [DataField]
    public TimeSpan HealInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>Stop if the client stops renewing its held input.</summary>
    [DataField]
    public TimeSpan InputTimeout = TimeSpan.FromSeconds(0.6);

    /// <summary>Energy consumed per second in either mode, including on healthy patients. Zero disables the cell requirement.</summary>
    [DataField]
    public float ChargePerSecond = 36f;

    /// <summary>Divides automatic-mode healing and clotting. Damage healing is rounded to whole units per second.</summary>
    [DataField]
    public float AutomaticRateDivisor = 3f;

    /// <summary>Bleeding rate removed per second in manual mode. Does not restore blood volume or bloodloss damage.</summary>
    [DataField]
    public float BleedReductionPerSecond = 1f;

    /// <summary>Budgets distributed among existing injuries within each damage group.</summary>
    [DataField]
    public Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> GroupHealing = new();

    /// <summary>Additional budgets for individual damage types, independent of group budgets.</summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> TypeHealing = new();

    /// <summary>Eligible patients. Mob state and damageable components are always required.</summary>
    [DataField]
    public EntityWhitelist? TargetWhitelist;

    /// <summary>Appearance of the persistent link.</summary>
    [DataField(required: true)]
    public ProtoId<EntityLinkVisualPrototype> BeamStyle;

    /// <summary>Continuous positional sound, played once and looped while the beam is connected, including to healthy patients.</summary>
    [DataField]
    public SoundSpecifier? HealingSound;

    /// <summary>Short sound when treatment starts.</summary>
    [DataField]
    public SoundSpecifier? StartSound;

    /// <summary>Short sound when treatment ends.</summary>
    [DataField]
    public SoundSpecifier? StopSound;

    /// <summary>Feedback when switching treatment modes.</summary>
    [DataField]
    public SoundSpecifier? ModeSound;
}
