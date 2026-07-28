/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Shared._CE.ZLevels.Damage.SafeFalling;

public sealed class CESafeFallingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        if (!CESharedZLevelsSystem.ZLevelsEnabled) // Exodus-disable-z-levels
            return;

        SubscribeLocalEvent<CESafeFallingComponent, CEZFallingDamageCalculateEvent>(OnFallingDamageCalculate);
    }

    private void OnFallingDamageCalculate(Entity<CESafeFallingComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        if (args.Fallen == ent.Owner)
            return;

        args.DamageMultiplier *= ent.Comp.DamageMultiplier;
        args.StunMultiplier *= ent.Comp.StunMultiplier;
    }
}
