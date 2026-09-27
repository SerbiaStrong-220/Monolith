using Content.Shared._Exodus.Weapons.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Wieldable;

public abstract partial class SharedWieldableSystem
{
    private void ApplyAttachmentWieldBonus(Entity<GunWieldBonusComponent> bonus, ref GunRefreshModifiersEvent args)
    {
        var ev = new GunWieldBonusRefreshEvent(bonus.Comp.MinAngle, bonus.Comp.MaxAngle,
            bonus.Comp.AngleDecay, bonus.Comp.AngleIncrease);
        RaiseLocalEvent(bonus, ref ev);
        args.MinAngle = Math.Max(args.MinAngle + ev.MinAngle, 0);
        args.MaxAngle = Math.Max(args.MaxAngle + ev.MaxAngle, args.MinAngle);
        args.AngleDecay += ev.AngleDecay;
        args.AngleIncrease += ev.AngleIncrease;
    }
}
