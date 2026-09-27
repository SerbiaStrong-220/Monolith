using Content.Shared._Exodus.Visuals;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Medical;

/// <summary>A held tool that maintains a healing beam while aimed at a patient.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MedicalBeamGunComponent : Component
{
    /// <summary>Maximum distance between the user and patient, in meters.</summary>
    [DataField]
    public float Range = 6f;

    /// <summary>Interval between server-side treatment and obstruction checks.</summary>
    [DataField]
    public TimeSpan HealInterval = TimeSpan.FromSeconds(0.2);

    /// <summary>Stop if the client stops renewing its held input.</summary>
    [DataField]
    public TimeSpan InputTimeout = TimeSpan.FromSeconds(0.6);

    /// <summary>Energy consumed per second of treatment. Zero disables the cell requirement.</summary>
    [DataField]
    public float ChargePerSecond = 12f;

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
}
