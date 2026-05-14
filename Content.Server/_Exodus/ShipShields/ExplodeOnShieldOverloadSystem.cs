using Content.Server.Explosion.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Triggers an explosion on shield emitters whose accumulated damage crosses
/// the configured DamageLimit (forced overload). Skips overloads caused by power loss alone.
/// </summary>
public sealed class ExplodeOnShieldOverloadSystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ExplodeOnShieldOverloadComponent, ShipShieldEmitterComponent>();
        while (query.MoveNext(out var uid, out var explode, out var emitter))
        {
            if (explode.Triggered)
                continue;

            var overLimit = emitter.Damage > emitter.DamageLimit;

            if (overLimit && !explode.WasOverLimit)
            {
                explode.Triggered = true;
                var ev = new ShipShieldOverloadedEvent();
                RaiseLocalEvent(uid, ref ev);

                _explosion.QueueExplosion(
                    uid,
                    explode.ExplosionType,
                    explode.TotalIntensity,
                    explode.IntensitySlope,
                    explode.MaxTileIntensity);
            }

            explode.WasOverLimit = overLimit;
        }
    }
}
