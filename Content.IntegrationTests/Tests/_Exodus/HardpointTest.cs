using System.Numerics;
using Content.Shared._Exodus.Weapons.Hardpoints;
using Content.Shared._Mono.ShipGuns;
using Content.Shared.Interaction.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ExodusHardpointSystem))]
public sealed class HardpointTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusHardpointTestGunBase
          components:
          - type: Physics
            bodyType: Static
          - type: Transform
            anchored: true
          - type: Gun
            fireRate: 2
            selectedMode: Burst
            availableModes: [Burst]
            burstFireRate: 2
            shotsPerBurst: 2
            burstCooldown: 10
            minAngle: 0
            maxAngle: 0
          - type: BallisticAmmoProvider
            proto: Cartridge357_magnumFMJ
            capacity: 10
            infiniteUnspawned: true

        - type: entity
          id: ExodusHardpointTestGun
          parent: ExodusHardpointTestGunBase
          components:
          - type: ExodusHardpointWeapon
            class: Ballistic
            size: Light
        """;

    [Test]
    public async Task MountCompatibilityComposesWithBothCooldownEvents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var transform = entities.System<SharedTransformSystem>();
            var gun = entities.SpawnEntity("ExodusHardpointTestGun", origin);
            var weapon = entities.GetComponent<ExodusHardpointWeaponComponent>(gun);
            AssertRate(0.5f);

            var mount = entities.SpawnEntity("HardpointBallisticLight", origin);
            var platform = entities.GetComponent<ExodusHardpointComponent>(mount);
            Assert.That(entities.GetComponent<TransformComponent>(gun).Anchored, Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(mount).Anchored, Is.True);

            // Rows are weapon sizes, columns are platform sizes, both from Superlight to Superheavy.
            float[,] rates =
            {
                { 1f, 1f, 1f, 1f, 1f },
                { 0.75f, 1f, 1f, 1f, 1f },
                { 0.5f, 0.75f, 1f, 1f, 1f },
                { 0.5f, 0.5f, 0.75f, 1f, 1f },
                { 0.5f, 0.5f, 0.5f, 0.75f, 1f },
            };
            foreach (var weaponSize in Enum.GetValues<ShipGunClass>())
            {
                weapon.Size = weaponSize;
                foreach (var mountSize in Enum.GetValues<ShipGunClass>())
                {
                    platform.Size = mountSize;
                    AssertRate(rates[(int) weaponSize, (int) mountSize]);
                }
            }

            foreach (var weaponClass in Enum.GetValues<ExodusHardpointClass>())
            {
                weapon.Class = weaponClass;
                foreach (var mountClass in Enum.GetValues<ExodusHardpointClass>())
                {
                    platform.Class = mountClass;
                    AssertRate(weaponClass == mountClass || weaponClass == ExodusHardpointClass.Universal ||
                        mountClass == ExodusHardpointClass.Universal ? 1f : 0.5f);
                }
            }

            weapon.Class = ExodusHardpointClass.Ballistic;
            weapon.Size = ShipGunClass.Light;
            platform.Class = ExodusHardpointClass.Ballistic;
            platform.Size = ShipGunClass.Superlight;
            AssertRate(0.75f);

            // A better overlapping platform replaces the weaker rate instead of stacking it.
            var secondMount = entities.SpawnEntity("HardpointBallisticSuperheavy", origin);
            AssertRate(1f);
            entities.DeleteEntity(secondMount);
            AssertRate(0.75f);

            transform.Unanchor(mount);
            AssertRate(0.5f);
            transform.AnchorEntity(mount);
            AssertRate(0.75f);
            transform.Unanchor(gun);
            AssertRate(0.5f);
            transform.AnchorEntity(gun);
            AssertRate(0.75f);
            entities.DeleteEntity(mount);
            AssertRate(0.5f);
            entities.SpawnEntity("HardpointBallisticLight", origin);
            AssertRate(1f);

            // Leaving the grid must not retain a reference to the old mount.
            transform.SetCoordinates(gun, new EntityCoordinates(map.MapUid, Vector2.Zero));
            AssertRate(0.5f);

            var ordinary = entities.SpawnEntity("ExodusHardpointTestGunBase", origin);
            var ordinaryShot = new QueryFireRateMultiplierEvent(3f);
            var ordinaryBurst = new QueryGunReloadCooldownMultiplierEvent(4f);
            entities.EventBus.RaiseLocalEvent(ordinary, ref ordinaryShot);
            entities.EventBus.RaiseLocalEvent(ordinary, ref ordinaryBurst);
            Assert.That(ordinaryShot.ReloadTimeMul, Is.EqualTo(3f));
            Assert.That(ordinaryBurst.ReloadCooldownMultiplier, Is.EqualTo(4f));

            void AssertRate(float rate)
            {
                // Existing modifiers (for example nebula effects) must remain multiplicative.
                var shot = new QueryFireRateMultiplierEvent(3f);
                var burst = new QueryGunReloadCooldownMultiplierEvent(4f);
                entities.EventBus.RaiseLocalEvent(gun, ref shot);
                entities.EventBus.RaiseLocalEvent(gun, ref burst);
                Assert.That(shot.ReloadTimeMul, Is.EqualTo(3f / rate).Within(0.0001f));
                Assert.That(burst.ReloadCooldownMultiplier, Is.EqualTo(4f / rate).Within(0.0001f));
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(null, 0.5f)]
    [TestCase("HardpointEnergyLight", 0.5f)]
    [TestCase("HardpointBallisticSuperlight", 0.75f)]
    [TestCase("HardpointBallisticLight", 1f)]
    public async Task BurstUsesMountRateForShotsAndRecovery(string mountPrototype, float rate)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid weapon = default;
        var started = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            if (mountPrototype != null)
                entities.SpawnEntity(mountPrototype, origin);

            weapon = entities.SpawnEntity("ExodusHardpointTestGun", origin);
            var gun = entities.GetComponent<GunComponent>(weapon);
            started = timing.CurTime;
            gun.NextFire = started;
            entities.System<SharedGunSystem>().AttemptShoot(weapon, weapon, gun,
                origin.Offset(new Vector2(0, 10)));

            Assert.That(gun.BurstActivated, Is.True);
            Assert.That((gun.NextFire - started).TotalSeconds, Is.EqualTo(0.5 / rate).Within(0.001));
        });

        await server.WaitRunTicks(timing.TickRate * 2);
        await server.WaitAssertion(() =>
        {
            var gun = entities.GetComponent<GunComponent>(weapon);
            Assert.That(gun.BurstActivated, Is.False);
            Assert.That((gun.NextFire - started).TotalSeconds, Is.EqualTo(11.0 / rate).Within(0.05));

            var recovery = gun.NextFire;
            entities.EventBus.RaiseLocalEvent(weapon, new UseInHandEvent(weapon));
            Assert.That(gun.NextFire, Is.EqualTo(recovery), "Manual cycling must not bypass the recovery penalty.");
        });

        await pair.CleanReturnAsync();
    }
}
