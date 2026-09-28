using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Network;

namespace Content.Shared._Goobstation.Weapons.Multihit;

public sealed partial class ActiveMultihitSystem : EntitySystem // Exodus: generated dependency injection.
{
    [Dependency] private INetManager _net = default!; // Exodus: generated dependency injection.

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveMultihitComponent, MeleeHitEvent>(OnHit, after: new[] { typeof(MultihitSystem) });
    }

    private void OnHit(Entity<ActiveMultihitComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        if (Math.Abs(ent.Comp.DamageMultiplier - 1f) > 0.01f)
        {
            var modifierSet = new DamageModifierSet
            {
                Coefficients = args.BaseDamage.DamageDict
                    .Select(x => new KeyValuePair<string, float>(x.Key, ent.Comp.DamageMultiplier))
                    .ToDictionary(),
            };

            args.ModifiersList.Add(modifierSet);
        }

        RemComp(ent.Owner, ent.Comp);
    }

}
