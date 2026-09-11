using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;

namespace Content.Shared._Exodus.Virology.Conditions;

/// <summary>Allows fragile or already wounded hosts to reach a severe stage before symptom damage kills them.</summary>
public sealed partial class VirusDamageProgressCondition : VirusProgressCondition
{
    [DataField]
    public float DeathThresholdFraction = 0.25f;

    protected override bool Condition(in VirusProgressArgs args)
    {
        if (!args.EntityManager.TryGetComponent<DamageableComponent>(args.Carrier, out var damage)
            || !args.EntityManager.System<MobThresholdSystem>()
                .TryGetThresholdForState(args.Carrier, MobState.Dead, out var threshold))
            return false;

        return damage.TotalDamage.Float() >= threshold.Value.Float() * DeathThresholdFraction;
    }
}
