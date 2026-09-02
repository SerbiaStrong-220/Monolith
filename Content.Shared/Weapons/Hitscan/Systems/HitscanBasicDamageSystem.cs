using Content.Shared.Damage;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicDamageComponent, HitscanRaycastFiredEvent>(OnHitscanHit, after: [ typeof(HitscanReflectSystem) ]);
    }

    private void OnHitscanHit(Entity<HitscanBasicDamageComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled) // Mono
            return;

        var dmg = ent.Comp.Damage * _damage.UniversalHitscanDamageModifier;

        foreach (var hitEntity in args.HitEntities) // Mono edit
        {
            var damageDealt = _damage.TryChangeDamage(hitEntity,
                dmg,
                origin: args.Shooter, // Exodus: preserve shooter attribution for every upstream multi-hit target.
                tool: args.Gun, // Exodus: preserve the firing weapon as the damage tool.
                armorPenetration: ent.Comp.ArmorPenetration,
                ignoreResistances: ent.Comp.IgnoreResistances); // Mono - AP

            if (damageDealt == null)
                continue; // Exodus: one invalid multi-hit target must not suppress later hits.

            var damageEvent = new HitscanDamageDealtEvent
            {
                Target = hitEntity, // Mono
                DamageDealt = damageDealt,
            };

            RaiseLocalEvent(ent, ref damageEvent);
        }
    }
}
