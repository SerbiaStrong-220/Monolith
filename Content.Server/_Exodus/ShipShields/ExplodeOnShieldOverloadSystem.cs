using Content.Server.Explosion.EntitySystems;
using Content.Shared._Exodus.ShipShields;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Queues an explosion when a damage overload remains unresolved by the shield's safety systems.
/// The overload context distinguishes damage from a genuine power loss before the receiver load changes.
/// </summary>
public sealed partial class ExplodeOnShieldOverloadSystem : EntitySystem
{
    [Dependency] private ExplosionSystem _explosion = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExplodeOnShieldOverloadComponent, ShipShieldOverloadAttemptEvent>(
            OnOverloadAttempt,
            after: new[] { typeof(CdmShieldReserveSystem), typeof(LayeredShipShieldSystem) });
    }

    private void OnOverloadAttempt(
        Entity<ExplodeOnShieldOverloadComponent> ent,
        ref ShipShieldOverloadAttemptEvent args)
    {
        if (args.Cancelled ||
            args.Cause != ShipShieldOverloadCause.Damage ||
            !args.PoweredBeforeLoad ||
            ent.Comp.Triggered)
        {
            return;
        }

        ent.Comp.Triggered = true;

        _explosion.QueueExplosion(
            ent.Owner,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.IntensitySlope,
            ent.Comp.MaxTileIntensity);
    }
}
