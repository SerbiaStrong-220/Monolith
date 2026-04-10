using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared.Projectiles;
using Prometheus;
using Robust.Shared.Configuration;

namespace Content.Server._Exodus.Adminbus.Crutches.BulletCounter;

public sealed partial class BulletCounterSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;

    private static readonly Gauge BulletsCountGauge = Metrics.CreateGauge(
        "exds_bullets_total_count",
        "Number of currently existing bullets.");
    private static readonly Gauge BulletsCountShipGauge = Metrics.CreateGauge(
        "exds_bullets_ship_count",
        "Number of currently existing bullets fired by ship weapons.");
    private static readonly Gauge BulletsCountOtherGauge = Metrics.CreateGauge(
        "exds_bullets_other_count",
        "Number of currently existing bullets that isn't fired by ship weapons.");

    private bool _enabled;
    private EntityQuery<ShipWeaponProjectileComponent> _shipProjectileQuery;

    public override void Initialize()
    {
        base.Initialize();

        _shipProjectileQuery = GetEntityQuery<ShipWeaponProjectileComponent>();

        Subs.CVar(_config, XCVars.BulletCounterEnabled, value => _enabled = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        // TODO: instead of wasting precious CPU time for counting, it's should be placed in engine to print out statistics for every component type which will be easier for CPU without extra iterations

        var enumerator = EntityQueryEnumerator<ProjectileComponent>();
        var total = 0;
        var ship = 0;
        var other = 0;

        while (enumerator.MoveNext(out var uid, out _))
        {
            total++;

            if (_shipProjectileQuery.HasComp(uid))
                ship++;
            else
                other++;
        }

        BulletsCountGauge.Set(total);
        BulletsCountShipGauge.Set(ship);
        BulletsCountOtherGauge.Set(other);
    }
}
