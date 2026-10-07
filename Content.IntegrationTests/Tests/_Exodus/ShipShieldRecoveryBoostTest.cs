using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Emp;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Power.Components;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipShieldsSystem))]
public sealed class ShipShieldRecoveryBoostTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusShieldRecoveryTestProjectile
          components:
          - type: Projectile
            damage:
              types:
                Heat: 100
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task AcceptedHitsRestartTheNineSecondRecoveryDelay(bool projectile)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        Entity<ShipShieldEmitterComponent> generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorAegis"));
        await PoolManager.WaitUntil(server, () => generator.Comp.Shield != null);
        await server.WaitAssertion(() =>
        {
            ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, projectile);
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f));
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f),
                "Recovery must remain at its base rate before nine quiet seconds.");
            ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, projectile);
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f),
            "A new hit must restart the delay, even after the original delay would have expired."));
        await pair.RunSeconds(6f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f)));
        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f),
                "Nine seconds without hits must add 40 percent to active recovery.");
            ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, projectile);
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f),
                "A hit must immediately cancel an already active recovery boost.");
        });

        await pair.RunSeconds(9.2f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectedHitsDoNotRestartTheRecoveryDelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        Entity<ShipShieldEmitterComponent> generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorAegis"));
        await PoolManager.WaitUntil(server, () => generator.Comp.Shield != null);
        await server.WaitAssertion(() => ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, false));
        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            var outside = new MapCoordinates(map.MapCoords.Position + new Vector2(1000f, 1000f), map.MapId);
            var hit = new ShipShieldHitAttemptEvent(outside, 100f, false);
            em.EventBus.RaiseLocalEvent(map.Grid, ref hit);
            Assert.That(hit.Absorbed, Is.False);
        });
        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f)));
        await pair.CleanReturnAsync();
    }

    [TestCase("disabled", 1050f)]
    [TestCase("recharging", 4200f)]
    [TestCase("overloaded", 1050f)]
    [TestCase("overload-ending", 1050f)]
    [TestCase("missing-field", 1050f)]
    public async Task RecoveryBoostDoesNotChangeInactiveRecovery(string state, float expectedRecovery)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        Entity<ShipShieldEmitterComponent> generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorAegis"));
        await PoolManager.WaitUntil(server, () => generator.Comp.Shield != null);
        await server.WaitAssertion(() => ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, false));
        await pair.RunSeconds(9.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f));

            switch (state)
            {
                case "disabled":
                    // Disabling is immediate, while Powered changes on the next power-network update.
                    em.System<PowerReceiverSystem>().SetPowerDisabled(generator, true);
                    break;
                case "recharging":
                    generator.Comp.Recharging = true;
                    break;
                case "overloaded":
                    generator.Comp.OverloadAccumulator = 30f;
                    break;
                case "overload-ending":
                    generator.Comp.OverloadAccumulator = 1f;
                    break;
                case "missing-field":
                    em.DeleteEntity(generator.Comp.Shield!.Value);
                    em.RemoveComponent<ShipShieldedComponent>(map.Grid);
                    generator.Comp.Shield = null;
                    generator.Comp.Shielded = null;
                    break;
                default:
                    Assert.Fail($"Unknown shield state: {state}");
                    break;
            }

            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(expectedRecovery).Within(0.01f),
                "An inactive shield must retain its existing recovery rate without the active boost.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldBlockedEmpRestartsTheRecoveryDelay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        Entity<ShipShieldEmitterComponent> generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorAegis"));
        await PoolManager.WaitUntil(server, () => generator.Comp.Shield != null);
        await pair.RunSeconds(9.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f));
            var batteryUid = em.SpawnEntity(null, map.GridCoords);
            var battery = em.AddComponent<BatteryComponent>(batteryUid);
            var batteries = em.System<BatterySystem>();
            batteries.SetMaxCharge(batteryUid, 1000f, battery);
            batteries.SetCharge(batteryUid, 1000f, battery);

            Assert.That(em.System<EmpSystem>().TryEmpEffects(batteryUid, 100f, TimeSpan.FromSeconds(5)), Is.False);
            Assert.That(battery.CurrentCharge, Is.EqualTo(1000f));
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f),
                "An EMP stopped by the active field must interrupt accelerated recovery.");
        });
        await pair.RunSeconds(8f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1050f).Within(0.01f)));
        await pair.RunSeconds(1.2f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(1470f).Within(0.01f)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LibertyKeepsItsBaseRecoveryAfterNineQuietSeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        Entity<ShipShieldEmitterComponent> generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorUnsa"));
        await PoolManager.WaitUntil(server, () => generator.Comp.Shield != null);
        await server.WaitAssertion(() =>
        {
            ApplyHit(em, generator, map.Grid, map.GridCoords, map.MapCoords, false);
            Assert.That(MeasureRecovery(em, generator), Is.EqualTo(825f).Within(0.01f));
        });
        await pair.RunSeconds(9.2f);
        await server.WaitAssertion(() => Assert.That(MeasureRecovery(em, generator), Is.EqualTo(825f).Within(0.01f)));
        await pair.CleanReturnAsync();
    }

    private static Entity<ShipShieldEmitterComponent> SpawnGenerator(
        IEntityManager em,
        EntityCoordinates coordinates,
        EntProtoId prototype)
    {
        var uid = em.SpawnEntity(prototype, coordinates);
        em.System<PowerReceiverSystem>().SetNeedsPower(uid, false);
        return (uid, em.GetComponent<ShipShieldEmitterComponent>(uid));
    }

    private static void ApplyHit(
        IEntityManager em,
        Entity<ShipShieldEmitterComponent> generator,
        EntityUid grid,
        EntityCoordinates coordinates,
        MapCoordinates point,
        bool projectile)
    {
        var previousDamage = generator.Comp.Damage;
        if (projectile)
        {
            var uid = em.SpawnEntity("ExodusShieldRecoveryTestProjectile", coordinates);
            var component = em.GetComponent<ProjectileComponent>(uid);
            var hit = new ShipShieldsSystem.ShieldDeflectedEvent(uid, component);
            em.EventBus.RaiseLocalEvent(generator.Owner, ref hit);
            Assert.That(component.ProjectileSpent, Is.True);
        }
        else
        {
            var hit = new ShipShieldHitAttemptEvent(point, 100f, false);
            em.EventBus.RaiseLocalEvent(grid, ref hit);
            Assert.That(hit.Absorbed, Is.True);
        }

        Assert.That(generator.Comp.Damage, Is.GreaterThan(previousDamage));
    }

    private static float MeasureRecovery(IEntityManager em, Entity<ShipShieldEmitterComponent> generator)
    {
        // Keep enough damage to measure the full recovery step without triggering an overload.
        generator.Comp.Damage = 20000f;
        generator.Comp.Accumulator = 1.5f;
        em.System<ShipShieldsSystem>().Update(0f);
        return 20000f - generator.Comp.Damage;
    }
}
