using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed class HealOverTimeSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Queue<(EntityUid Target, FixedPoint2 Amount)> _pending = new();

    /// <summary>
    /// Queues heal to be applied next Update.
    /// </summary>
    public void QueueHeal(EntityUid target, FixedPoint2 amount)
    {
        _pending.Enqueue((target, amount));
    }

    /// <summary>
    /// Adds or refreshes a heal-over-time buff.
    /// </summary>
    public void Apply(EntityUid target, FixedPoint2 healPerSecond, TimeSpan duration)
    {
        var comp = EnsureComp<HealOverTimeComponent>(target);
        var now = _timing.CurTime;
        comp.HealPerSecond = healPerSecond;
        comp.EndTime = now + duration;
        comp.NextTick = now + comp.Interval;
    }

    /// <summary>
    /// Heals damaged entity, spread across the current damage types.
    /// </summary>
    public void Heal(Entity<DamageableComponent?> target, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !Resolve(target, ref target.Comp, false))
            return;

        var total = target.Comp.TotalDamage;
        if (total <= FixedPoint2.Zero)
            return;

        var heal = new DamageSpecifier();
        foreach (var (type, value) in target.Comp.Damage.DamageDict)
        {
            if (value <= FixedPoint2.Zero)
                continue;

            heal.DamageDict[type] = -(value / total) * amount;
        }

        _damageable.TryChangeDamage(target, heal, ignoreResistances: true, interruptsDoAfters: false, target.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        while (_pending.TryDequeue(out var pending))
        {
            if (!Deleted(pending.Target))
                Heal(pending.Target, pending.Amount);
        }

        var query = EntityQueryEnumerator<HealOverTimeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
            {
                RemCompDeferred<HealOverTimeComponent>(uid);
                continue;
            }

            if (now < comp.NextTick)
                continue;

            comp.NextTick += comp.Interval;
            Heal(uid, comp.HealPerSecond * (float) comp.Interval.TotalSeconds);
        }
    }
}
