using System.Collections.Generic;
using System.Numerics;
using Content.Client._Exodus.Effects;
using Content.Server._Exodus.Weapons.GravityPulse;
using Content.Shared._Exodus.Weapons.DistanceFalloff;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(GravityPulseSystem))]
[Ignore("Disabled pending updates to gravity weapon test scenes and firing cooldown handling.")]
public sealed class GravityPulseTest
{
    // Fixed test scenes exercise mechanics independently of live weapon balance.
    // The loaded mech test reads its expected volley from the production prototype.
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusPulseTestProjectile
          parent: GravityPulseMech
          components:
          - type: Projectile
            damage:
              types:
                Blunt: 30
                Structural: 90
          - type: ProjectileKnockback
            knockback: 120
            rotateMultiplier: 0
            affectGrids: false
          - type: DistanceFalloff
            range: 8
            fullStrengthDistance: 2
            exponent: 1
          - type: ProjectileSpread
            proto: ExodusPulseTestProjectile
            count: 5
            spread: 30
          - type: TimedDespawn
            lifetime: 2

        - type: entity
          id: ExodusPulseTestGun
          parent: WeaponGravityProjector
          components:
          - type: GravityPulseLauncher
            shareHits: true
          - type: Gun
            projectileSpeed: 7
            fireRate: 1
            damageModifier: 1
            minAngle: 0
            maxAngle: 0
          - type: Battery
            maxCharge: 100
            startingCharge: 100
          - type: ProjectileBatteryAmmoProvider
            proto: ExodusPulseTestProjectile
            fireCost: 1

        - type: entity
          id: ExodusPulseTestTarget
          components:
          - type: Damageable
            damageContainer: StructuralInorganic
          - type: Physics
            bodyType: Static
          - type: Fixtures
            fixtures:
              body:
                shape: !type:PhysShapeAabb
                  bounds: "-0.5,-0.5,0.5,0.5"
                density: 1000
                hard: true
                layer: [BulletImpassable]
                mask: []

        - type: entity
          id: ExodusPulseTestMovable
          parent: ExodusPulseTestTarget
          components:
          - type: Physics
            bodyType: Dynamic
        """;

    [TestCase(0)]
    [TestCase(37)]
    public async Task HeldGunLaunchesAndHitsInWorldDirection(float gridDegrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        var transform = entities.System<SharedTransformSystem>();
        var direction = -Vector2.UnitY;
        EntityUid target = default;
        EntityUid shooter = default;
        await server.WaitAssertion(() =>
        {
            FillGrid(entities, map.Grid, map.Tile.Tile);
            transform.SetWorldRotation(map.Grid, Angle.FromDegrees(gridDegrees));
            var origin = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var start = transform.ToMapCoordinates(origin);
            shooter = entities.SpawnEntity("MobHuman", origin);
            var weapon = entities.SpawnEntity("ExodusPulseTestGun", origin);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(shooter, weapon), Is.True);
            target = entities.SpawnEntity("ExodusPulseTestTarget", start.Offset(direction * 3));
            entities.System<SharedGunSystem>().AttemptShoot(shooter, weapon,
                entities.GetComponent<GunComponent>(weapon), transform.ToCoordinates(start.Offset(direction * 10)));
            var pulses = FindPulses(entities, weapon);
            Assert.That(pulses, Has.Count.EqualTo(5));
            foreach (var uid in pulses)
            {
                Assert.That(entities.GetComponent<DistanceFalloffComponent>(uid).Origin, Is.Not.Null);
                var velocity = entities.GetComponent<PhysicsComponent>(uid).LinearVelocity;
                Assert.That(Vector2.Dot(velocity, direction), Is.GreaterThan(0f));
                Assert.That(entities.GetComponent<TransformComponent>(uid).ParentUid, Is.EqualTo(map.Grid.Owner));
            }
        });
        await server.WaitRunTicks(40);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, target), Is.GreaterThan(0));
            var wounds = entities.GetComponent<DamageableComponent>(shooter).Damage.DamageDict;
            Assert.That(wounds.GetValueOrDefault("Blunt").Float(), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientReceivesWaveAndPlaysImpactEffect()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = pair;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        List<EntityUid> pulses = [];
        await server.WaitAssertion(() =>
        {
            FillGrid(entities, map.Grid, map.Tile.Tile);
            var origin = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var shooter = entities.SpawnEntity("MobHuman", origin);
            var weapon = entities.SpawnEntity("ExodusPulseTestGun", origin);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(shooter, weapon), Is.True);
            entities.System<SharedGunSystem>().AttemptShoot(shooter, weapon,
                entities.GetComponent<GunComponent>(weapon), origin.Offset(new Vector2(0, -10)));
            pulses = FindPulses(entities, weapon);
            Assert.That(pulses, Has.Count.EqualTo(5));
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var clientEntities = client.ResolveDependency<IEntityManager>();
            foreach (var pulse in pulses)
            {
                var uid = pair.ToClientUid(pulse);
                var falloff = clientEntities.GetComponent<DistanceFalloffComponent>(uid);
                var xform = clientEntities.GetComponent<TransformComponent>(uid);
                Assert.That(falloff.Origin, Is.Not.Null);
                Assert.That(clientEntities.System<DistanceFalloffSystem>().TryGetStrength((uid, falloff),
                    xform.Coordinates, out var strength), Is.True);
                Assert.That(strength, Is.GreaterThan(0));
                Assert.That(clientEntities.GetComponent<SpriteComponent>(uid).Visible, Is.True);
                Assert.That(clientEntities.GetComponent<WaveDistortionVisualsComponent>(uid).Instance, Is.Not.Null);
            }

            // Exercise the actual impact handler: a missing Unshaded layer used to throw here.
            var wave = pair.ToClientUid(pulses[0]);
            var impact = clientEntities.GetComponent<ProjectileComponent>(wave).ImpactEffect;
            Assert.That(impact, Is.Not.Null);
            var coordinates = clientEntities.GetNetCoordinates(clientEntities.GetComponent<TransformComponent>(wave).Coordinates);
            clientEntities.EventBus.RaiseEvent(EventSource.Local, new ImpactEffectEvent(impact.Value, coordinates));
            var found = false;
            var animations = clientEntities.System<AnimationPlayerSystem>();
            var effects = clientEntities.EntityQueryEnumerator<AnimationPlayerComponent, MetaDataComponent>();
            while (effects.MoveNext(out var uid, out _, out var metadata))
            {
                if (metadata.EntityPrototype?.ID == impact.Value.Id)
                    found |= animations.HasRunningAnimation(uid, "impact-effect");
            }
            Assert.That(found, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(1f, 120f)]
    [TestCase(5f, 60f)]
    [TestCase(9f, 0f)]
    public async Task DistanceScalesDamageAndKnockback(float distance, float expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid pulse = default;
        await server.WaitAssertion(() =>
        {
            var target = entities.SpawnEntity("ExodusPulseTestMovable", map.MapCoords.Offset(Vector2.UnitX * distance));
            var weapon = entities.SpawnEntity("ExodusPulseTestGun", map.MapCoords);
            pulse = entities.SpawnEntity("ExodusPulseTestProjectile", map.MapCoords);
            entities.System<SharedGunSystem>().ShootProjectile(pulse, Vector2.UnitX, Vector2.Zero, weapon, speed: 8f);
            // Invoke the native hit before a physics tick can apply friction to the target.
            entities.System<SharedProjectileSystem>().ProjectileCollide(
                (pulse, entities.GetComponent<ProjectileComponent>(pulse), entities.GetComponent<PhysicsComponent>(pulse)), target);
            var body = entities.GetComponent<PhysicsComponent>(target);
            Assert.That(Damage(entities, target), Is.EqualTo(expected).Within(0.1f));
            Assert.That(body.LinearVelocity.X * body.Mass, Is.EqualTo(expected).Within(0.1f));
            Assert.That(body.LinearVelocity.Y, Is.Zero.Within(0.001f));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(pulse), Is.True));
        await pair.CleanReturnAsync();
    }

    [TestCase(true, 1)]
    [TestCase(false, 4)]
    public async Task VolleyLimitsHitsAndStopsAtWalls(bool shareHits, int firstHits)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid wall = default;
        EntityUid behind = default;
        await server.WaitAssertion(() =>
        {
            wall = entities.SpawnEntity("ExodusPulseTestTarget", map.MapCoords.Offset(Vector2.UnitX));
            behind = entities.SpawnEntity("ExodusPulseTestTarget", map.MapCoords.Offset(Vector2.UnitX * 3));
            var pulses = FireVolley(entities, map.MapCoords, shareHits);
            Assert.That(pulses, Has.Count.EqualTo(5));
            entities.DeleteEntity(pulses[0]);
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, wall), Is.EqualTo(120f * firstHits).Within(0.1f));
            Assert.That(Damage(entities, behind), Is.Zero);
            FireVolley(entities, map.MapCoords, shareHits);
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, wall), Is.EqualTo(120f * (firstHits + (shareHits ? 1 : 5))).Within(0.1f));
            Assert.That(Damage(entities, behind), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MountedGunFiresWithoutHittingItsChassis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        EntityUid mech = default;
        await server.WaitAssertion(() =>
        {
            FillGrid(entities, map.Grid, map.Tile.Tile);
            mech = entities.SpawnEntity("MechKharLoaded", map.GridCoords);
            var pilot = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.System<SharedMechSystem>().TryInsert(mech, pilot), Is.True);
            var component = entities.GetComponent<MechComponent>(mech);
            Assert.That(component.BatterySlot.ContainedEntity, Is.Not.Null);
            EntityUid? weapon = null;
            foreach (var equipment in component.EquipmentContainer.ContainedEntities)
            {
                if (entities.HasComponent<GravityPulseLauncherComponent>(equipment))
                    weapon = equipment;
            }
            Assert.That(weapon, Is.Not.Null);
            var gun = entities.GetComponent<GunComponent>(weapon.Value);
            entities.System<SharedGunSystem>().AttemptShoot(mech, weapon.Value, gun,
                map.GridCoords.Offset(new Vector2(0, -10)));
            foreach (var pulse in AssertConfiguredVolley(entities, prototypes, weapon.Value))
                Assert.That(entities.GetComponent<ProjectileComponent>(pulse).Shooter, Is.EqualTo(mech));
        });
        await server.WaitRunTicks(45);
        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(entities, mech), Is.Zero);
            Assert.That(entities.GetComponent<PhysicsComponent>(map.Grid).LinearVelocity, Is.EqualTo(Vector2.Zero));
        });
        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> AssertConfiguredVolley(IEntityManager entities, IPrototypeManager prototypes,
        EntityUid weapon)
    {
        var ammo = entities.GetComponent<ProjectileBatteryAmmoProviderComponent>(weapon);
        var prototype = prototypes.Index<EntityPrototype>(ammo.Prototype);
        var expectedCount = prototype.TryGetComponent<ProjectileSpreadComponent>(out var spread, entities.ComponentFactory)
            ? spread.Count
            : 1;
        var pulses = FindPulses(entities, weapon);
        Assert.That(pulses, Has.Count.EqualTo(expectedCount));
        Assert.That(pulses, Is.Not.Empty);
        foreach (var pulse in pulses)
        {
            Assert.That(entities.GetComponent<DistanceFalloffComponent>(pulse).Origin, Is.Not.Null);
            Assert.That(entities.GetComponent<PhysicsComponent>(pulse).LinearVelocity.LengthSquared(), Is.GreaterThan(0f));
        }
        return pulses;
    }

    private static float Damage(IEntityManager entities, EntityUid uid)
        => entities.GetComponent<DamageableComponent>(uid).TotalDamage.Float();

    private static void FillGrid(IEntityManager entities, Entity<MapGridComponent> grid, Tile tile)
    {
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -10; x <= 10; x++)
        for (var y = -10; y <= 10; y++)
            tiles.Add((new Vector2i(x, y), tile));
        entities.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
    }

    private static List<EntityUid> FindPulses(IEntityManager entities, EntityUid weapon)
    {
        var result = new List<EntityUid>();
        var query = entities.EntityQueryEnumerator<GravityPulseComponent, ProjectileComponent>();
        while (query.MoveNext(out var uid, out _, out var projectile))
        {
            if (projectile.Weapon == weapon)
                result.Add(uid);
        }
        return result;
    }

    private static List<EntityUid> FireVolley(IEntityManager entities, MapCoordinates origin, bool shareHits)
    {
        var weapon = entities.SpawnEntity("ExodusPulseTestGun", origin);
        entities.GetComponent<GravityPulseLauncherComponent>(weapon).ShareHits = shareHits;
        var pulse = entities.SpawnEntity("ExodusPulseTestProjectile", origin);
        var gun = entities.GetComponent<GunComponent>(weapon);
        var coordinates = entities.System<SharedTransformSystem>().ToCoordinates(origin);
        entities.System<SharedGunSystem>().Shoot(weapon, gun, pulse, coordinates,
            coordinates.Offset(Vector2.UnitX * 10f), out _);
        return FindPulses(entities, weapon);
    }
}
