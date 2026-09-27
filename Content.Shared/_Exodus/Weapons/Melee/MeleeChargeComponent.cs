using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Weapons.Melee;

/// <summary>Spends LimitedCharges to add configurable damage to individual melee hits.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MeleeChargeComponent : Component
{
    /// <summary>
    /// Extra damage for one charged hit, before armor and melee hit modifiers.
    /// With ConsumeAllCharges, this is the damage per charge instead.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier BonusDamage = new();

    /// <summary>Charge cost per target, or minimum required charges when consuming the entire counter.</summary>
    [DataField]
    public int ChargesPerHit = 1;

    /// <summary>Spend all stored charges on one target, multiplying BonusDamage by the number spent.</summary>
    [DataField]
    public bool ConsumeAllCharges;

    /// <summary>Localized effect name used by examination and the held item status.</summary>
    [DataField]
    public LocId EffectName = "melee-charge-effect-name";

    /// <summary>Require ItemToggle to be active before spending charges.</summary>
    [DataField]
    public bool RequiresActivation = true;

    /// <summary>Require the weapon to be held by its attacker.</summary>
    [DataField]
    public bool RequiresHeld = true;

    /// <summary>Optionally restrict charged hits to the two-handed stance.</summary>
    [DataField]
    public bool RequiresWield;

    /// <summary>Clear stored charges on deactivation.</summary>
    [DataField]
    public bool ResetOnDeactivate = true;

    /// <summary>Clear stored charges when leaving a hand, including storage and disarming.</summary>
    [DataField]
    public bool ResetOnHandUnequipped = true;

    /// <summary>Optional restriction on targets that can receive a charged hit.</summary>
    [DataField]
    public EntityWhitelist? TargetWhitelist;

    /// <summary>Sound played only after a charged hit actually deals damage.</summary>
    [DataField]
    public SoundSpecifier? HitSound;
}
