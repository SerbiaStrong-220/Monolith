using Content.Shared._Exodus.Chemistry;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Chemistry;

public sealed partial class CombatStimulantSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public bool TryApply(EntityUid uid, FixedPoint2 amount, TimeSpan durationPerUnit, TimeSpan maximumDuration,
        float movementMultiplier, float attackRateMultiplier)
    {
        if (amount <= FixedPoint2.Zero || durationPerUnit <= TimeSpan.Zero || maximumDuration <= TimeSpan.Zero ||
            !TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState != MobState.Alive)
            return false;

        var effect = EnsureComp<CombatStimulantComponent>(uid);
        var now = _timing.CurTime;
        var start = effect.ExpiresAt > now ? effect.ExpiresAt : now;
        var end = start + durationPerUnit * amount.Float();
        effect.ExpiresAt = end < now + maximumDuration ? end : now + maximumDuration;
        effect.MovementMultiplier = movementMultiplier;
        effect.AttackRateMultiplier = attackRateMultiplier;
        Dirty(uid, effect);
        _movement.RefreshMovementSpeedModifiers(uid);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CombatStimulantComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var effect, out var mob))
        {
            if (effect.ExpiresAt > _timing.CurTime && mob.CurrentState == MobState.Alive)
                continue;
            // Neutralize before deferred removal so neither movement nor attacks retain the bonus this tick.
            effect.ExpiresAt = TimeSpan.Zero;
            effect.MovementMultiplier = 1f;
            effect.AttackRateMultiplier = 1f;
            Dirty(uid, effect);
            _movement.RefreshMovementSpeedModifiers(uid);
            RemCompDeferred<CombatStimulantComponent>(uid);
        }
    }
}
