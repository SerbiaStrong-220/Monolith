using Content.Server._Crescent.ShipShields;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Triggers an explosion on shield emitters that are forced into overload by damage.
/// Overload from damage means either the power-draw cap (LoadDamage &gt;= MaxDraw)
/// or the hard damage cap (Damage &gt; DamageLimit) was crossed while the emitter was powered.
/// Skips overloads caused by pure power loss.
/// </summary>
public sealed class ExplodeOnShieldOverloadSystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ExplodeOnShieldOverloadComponent, ShipShieldEmitterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var explode, out var emitter, out var power))
        {
            if (explode.Triggered)
                continue;

            var overloadedByDamage = power.Powered
                                     && (ShipShieldsSystem.CalculateLoadDamage(emitter) >= emitter.MaxDraw
                                         || emitter.Damage > emitter.DamageLimit);

            if (overloadedByDamage && !explode.WasOverloadedByDamage)
            {
                explode.Triggered = true;

                _explosion.QueueExplosion(
                    uid,
                    explode.ExplosionType,
                    explode.TotalIntensity,
                    explode.IntensitySlope,
                    explode.MaxTileIntensity);
            }

            explode.WasOverloadedByDamage = overloadedByDamage;
        }
    }

}
