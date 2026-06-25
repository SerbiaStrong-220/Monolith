using Content.Server._Exodus.Power;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.Power.Components;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Mirrors a shield emitter's active state to its <see cref="PoweredRadiationSourceComponent"/>.
/// The emitter radiates only while it has an active shield entity on a grid (Shield != null).
/// </summary>
public sealed partial class RadiatingShieldEmitterSystem : EntitySystem
{
    [Dependency] private PoweredRadiationSourceSystem _poweredRadiation = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, PoweredRadiationSourceComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var rad))
        {
            var shouldBeActive = emitter.Shield != null;
            if (rad.Active == shouldBeActive)
                continue;

            _poweredRadiation.SetActive(uid, shouldBeActive, rad);
        }
    }
}
