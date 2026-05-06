using Content.Server.Stack;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Silicons.Borgs;

public sealed class BorgModuleStackRechargerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BorgModuleStackRechargerComponent, BorgModuleComponent, ItemBorgModuleComponent>();
        while (query.MoveNext(out var uid, out var recharge, out var module, out var itemModule))
        {
            if (!module.Installed)
                continue;

            if (recharge.RechargeInterval <= TimeSpan.Zero)
                continue;

            if (recharge.NextRecharge == TimeSpan.Zero)
            {
                recharge.NextRecharge = _timing.CurTime + recharge.RechargeInterval;
                continue;
            }

            if (_timing.CurTime < recharge.NextRecharge)
                continue;

            RechargeModuleItems((uid, itemModule), recharge.RechargeAmount, recharge.MaxCount);
            recharge.NextRecharge += recharge.RechargeInterval;

            if (recharge.NextRecharge < _timing.CurTime)
                recharge.NextRecharge = _timing.CurTime + recharge.RechargeInterval;
        }
    }

    private void RechargeModuleItems(Entity<ItemBorgModuleComponent> module, int amount, int maxCount)
    {
        if (amount <= 0 || maxCount <= 0)
            return;

        if (module.Comp.ProvidedContainer == null)
            return;

        foreach (var item in module.Comp.ProvidedItems.Values)
        {
            RechargeStack(item, amount, maxCount);
        }

        foreach (var item in module.Comp.ProvidedContainer.ContainedEntities)
        {
            RechargeStack(item, amount, maxCount);
        }
    }

    private void RechargeStack(EntityUid uid, int amount, int maxCount)
    {
        if (!TryComp<StackComponent>(uid, out var stack))
            return;

        maxCount = Math.Min(maxCount, _stack.GetMaxCount(stack));
        if (stack.Count > maxCount)
        {
            _stack.SetCount(uid, maxCount, stack);
            return;
        }

        if (stack.Count == maxCount)
            return;

        _stack.SetCount(uid, Math.Min(stack.Count + amount, maxCount), stack);
    }
}
