using System.Collections.Generic;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Emp;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;
using Content.Shared.Emp;
using Content.Shared.Mech.Components;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipShieldsSystem))]
public sealed class LibertyShipShieldTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusShieldTestEmpProjectile
          parent: BaseBulletTrigger
          components:
          - type: ShipWeaponProjectile
          - type: Projectile
            damage:
              types:
                Heat: 100
          - type: EmpOnTrigger
            range: 60
            energyConsumption: 1000
            disableDuration: 5
        """;

    [Test]
    public async Task LibertyCreatesFullFieldAndReplicatesRipples()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        EntityUid field = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorUnsa"));
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var emitter = em.GetComponent<ShipShieldEmitterComponent>(generator);
            field = emitter.Shield!.Value;
            var sentinelUid = em.Spawn("ShieldGeneratorSmall");
            var sentinel = em.GetComponent<ShipShieldEmitterComponent>(sentinelUid);
            var capacity = ShipShieldsSystem.CalculateDamageOverloadThreshold(emitter);
            var referenceCapacity = ShipShieldsSystem.CalculateDamageOverloadThreshold(sentinel);
            var chain = (ChainShape) em.GetComponent<FixturesComponent>(field).Fixtures["shield"].Shape;

            Assert.Multiple(() =>
            {
                Assert.That(capacity / referenceCapacity, Is.InRange(1.1f, 1.2f));
                Assert.That(em.HasComponent<DirectionalShipShieldEmitterComponent>(generator), Is.False);
                Assert.That(em.HasComponent<DirectionalShipShieldFieldComponent>(field), Is.False);
                Assert.That(chain.Vertices[0], Is.EqualTo(chain.Vertices[^1]));
                Assert.That(em.GetComponent<ShipShieldVisualsComponent>(field).RippleWidth, Is.GreaterThan(0f));
            });
            em.DeleteEntity(sentinelUid);
        });

        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var clientField = pair.ToClientUid(field);
            var visuals = pair.Client.EntMan.GetComponent<ShipShieldVisualsComponent>(clientField);
            Assert.That(visuals.RippleWidth, Is.EqualTo(8f));
            Assert.That(visuals.RippleSpeed, Is.EqualTo(0.75f));
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            ProtoId<ShaderPrototype> rippleShaderPrototype = "ShipShieldRipple";
            using var shader = prototypes.Index(rippleShaderPrototype).InstanceUnique();
            shader.SetParameter("waveSpeed", visuals.RippleSpeed);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ShieldGeneratorUnsa", true)]
    [TestCase("ShieldGeneratorSmall", false)]
    public async Task EmpProtectsOnlyEquipmentOnTheShieldedShip(string prototype, bool protectedFromEmp)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;

        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -3; x <= 3; x++)
            {
                for (var y = -3; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }

            generator = SpawnGenerator(em, map.GridCoords, prototype);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var transform = em.System<SharedTransformSystem>();
            transform.SetWorldRotation(map.Grid, Angle.FromDegrees(37));
            var batteries = new List<Entity<BatteryComponent>>();
            for (var direction = 0; direction < 4; direction++)
            {
                var position = Angle.FromDegrees(direction * 90).ToWorldVec() * 2f;
                var battery = SpawnBattery(em, new EntityCoordinates(map.Grid, position));
                Assert.That(em.GetComponent<TransformComponent>(battery).GridUid, Is.EqualTo(map.Grid.Owner));
                batteries.Add(battery);
            }

            var containerOwner = em.SpawnEntity(null, map.GridCoords);
            var containers = em.System<SharedContainerSystem>();
            var container = containers.EnsureContainer<Container>(containerOwner, "test");
            var contained = SpawnBattery(em, map.GridCoords);
            Assert.That(containers.Insert(contained.Owner, container), Is.True);
            batteries.Add(contained);

            // A different grid and loose equipment in space must not inherit this ship's immunity.
            var otherGrid = server.MapMan.CreateGridEntity(map.MapId);
            em.System<SharedMapSystem>().SetTile(otherGrid.Owner, otherGrid.Comp, Vector2i.Zero, map.Tile.Tile);
            transform.SetCoordinates(otherGrid, new EntityCoordinates(map.MapUid, 10, 0));
            var otherBattery = SpawnBattery(em, new EntityCoordinates(otherGrid, 0.5f, 0.5f));
            var spaceBattery = SpawnBattery(em, new EntityCoordinates(map.MapUid, 15, 0));
            // Protection follows grid membership even beyond the rendered contour.
            var distantGridBattery = SpawnBattery(em, new EntityCoordinates(map.Grid, 100, 0));
            transform.SetCoordinates(distantGridBattery, new EntityCoordinates(map.Grid, 100, 0));
            Assert.That(em.GetComponent<TransformComponent>(distantGridBattery).GridUid, Is.EqualTo(map.Grid.Owner));
            var emp = em.System<EmpSystem>();

            // Exercise all three pulse overloads, including the legacy four-argument coordinates overload.
            emp.EmpPulse(map.MapCoords, 150f, 100f, TimeSpan.FromSeconds(5));
            emp.EmpPulse(map.GridCoords, 150f, 100f, TimeSpan.FromSeconds(5));
            emp.EmpPulse(map.GridCoords, 150f, 100f, TimeSpan.FromSeconds(5), user: null);
            Assert.Multiple(() =>
            {
                foreach (var battery in batteries)
                    Assert.That(battery.Comp.CurrentCharge, Is.EqualTo(protectedFromEmp ? 1000f : 700f));

                Assert.That(otherBattery.Comp.CurrentCharge, Is.EqualTo(700f));
                Assert.That(spaceBattery.Comp.CurrentCharge, Is.EqualTo(700f));
                Assert.That(distantGridBattery.Comp.CurrentCharge, Is.EqualTo(protectedFromEmp ? 1000f : 700f));
                Assert.That(em.HasComponent<EmpDisabledComponent>(generator), Is.EqualTo(!protectedFromEmp));
            });

            // Direct EMP attacks must use the same protection, including items inside a container.
            Assert.That(emp.TryEmpEffects(contained, 100f, TimeSpan.FromSeconds(5)), Is.EqualTo(!protectedFromEmp));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmpProtectionIncludesMechBatteries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorUnsa"));
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var mech = em.SpawnEntity("MechRipleyBattery", map.GridCoords);
            var batteryUid = em.GetComponent<MechComponent>(mech).BatterySlot.ContainedEntity;
            Assert.That(batteryUid, Is.Not.Null);
            var battery = em.GetComponent<BatteryComponent>(batteryUid!.Value);
            var originalCharge = battery.CurrentCharge;
            Assert.That(originalCharge, Is.GreaterThan(0f));
            var emp = em.System<EmpSystem>();

            Assert.That(emp.TryEmpEffects(mech, 100f, TimeSpan.FromSeconds(5)), Is.False);
            Assert.That(battery.CurrentCharge, Is.EqualTo(originalCharge));

            em.System<PowerReceiverSystem>().SetPowerDisabled(generator, true);
            Assert.That(emp.TryEmpEffects(mech, 100f, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(battery.CurrentCharge, Is.EqualTo(Math.Max(0f, originalCharge - battery.MaxCharge / 2f)));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmpProtectionStopsOnPowerLossOverloadAndRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        Entity<BatteryComponent> battery = default;

        await server.WaitAssertion(() =>
        {
            generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorUnsa");
            battery = SpawnBattery(em, map.GridCoords);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var emp = em.System<EmpSystem>();
            Assert.That(emp.TryEmpEffects(battery, 100f, TimeSpan.FromSeconds(5)), Is.False);
            em.System<PowerReceiverSystem>().SetPowerDisabled(generator, true);
            Assert.That(emp.TryEmpEffects(battery, 100f, TimeSpan.FromSeconds(5)), Is.True,
                "Switching off must stop protection before the next shield update.");
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield == null);
        await server.WaitAssertion(() => em.System<PowerReceiverSystem>().SetPowerDisabled(generator, false));
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var emp = em.System<EmpSystem>();
            Assert.That(emp.TryEmpEffects(battery, 100f, TimeSpan.FromSeconds(5)), Is.False);
            var emitter = em.GetComponent<ShipShieldEmitterComponent>(generator);
            var overload = new ShipShieldHitAttemptEvent(map.MapCoords, emitter.MaxDraw + 1f, false);
            em.EventBus.RaiseLocalEvent(map.Grid, ref overload);
            Assert.That(overload.Absorbed, Is.True);
            Assert.That(emitter.OverloadAccumulator, Is.GreaterThan(0f));
            Assert.That(emp.TryEmpEffects(battery, 100f, TimeSpan.FromSeconds(5)), Is.True);
            em.DeleteEntity(generator);
            Assert.That(emp.TryEmpEffects(battery, 100f, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(battery.Comp.CurrentCharge, Is.EqualTo(700f));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task EmpProjectilesAreInterceptedFromEveryDirection(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        EntityUid projectile = default;
        Entity<BatteryComponent> battery = default;
        Entity<BatteryComponent> unprotected = default;

        await server.WaitAssertion(() =>
        {
            generator = SpawnGenerator(em, map.GridCoords, "ShieldGeneratorUnsa");
            battery = SpawnBattery(em, map.GridCoords);
            unprotected = SpawnBattery(em, new EntityCoordinates(map.MapUid, 10, 10));
            em.System<SharedTransformSystem>().SetWorldRotation(map.Grid, Angle.FromDegrees(37));
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var direction = Angle.FromDegrees(degrees).ToWorldVec();
            var origin = new EntityCoordinates(map.MapUid, direction * 35f);
            var gun = em.SpawnEntity(null, origin);
            projectile = em.SpawnEntity("ExodusShieldTestEmpProjectile", origin);
            em.System<SharedGunSystem>().ShootProjectile(projectile, -direction, Vector2.Zero, gun, speed: 20f);
        });
        await PoolManager.WaitUntil(server, () => em.Deleted(projectile), maxTicks: 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ShipShieldEmitterComponent>(generator).Damage, Is.GreaterThan(0f));
            Assert.That(battery.Comp.CurrentCharge, Is.EqualTo(1000f));
            Assert.That(unprotected.Comp.CurrentCharge, Is.Zero, "The intercepted projectile must actually emit its EMP.");
            Assert.That(em.HasComponent<EmpDisabledComponent>(generator), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnGenerator(IEntityManager em, EntityCoordinates coordinates, EntProtoId prototype)
    {
        var generator = em.SpawnEntity(prototype, coordinates);
        em.System<PowerReceiverSystem>().SetNeedsPower(generator, false);
        return generator;
    }

    private static Entity<BatteryComponent> SpawnBattery(IEntityManager em, EntityCoordinates coordinates)
    {
        var uid = em.SpawnEntity(null, coordinates);
        var battery = em.AddComponent<BatteryComponent>(uid);
        var system = em.System<BatterySystem>();
        system.SetMaxCharge(uid, 1000f, battery);
        system.SetCharge(uid, 1000f, battery);
        return (uid, battery);
    }
}
