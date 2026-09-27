using System.Numerics;
using Content.Shared._Exodus.Weapons.Melee;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(MeleeChargeSystem))]
public sealed class MeleeChargeTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusTestChargedCrowbar
          parent: Crowbar
          components:
          - type: LimitedCharges
            maxCharges: 5
            charges: 5
          - type: MeleeCharge
            requiresActivation: false
            chargesPerHit: 2
            bonusDamage:
              types:
                Heat: 6
        """;

    [Test]
    public async Task ReflectionRequiresPoweredWieldAndChargesOnlyOncePerShot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var shooter = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weapon = entities.SpawnEntity("WeaponEnergyHalberdMARSOC", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var toggle = entities.System<ItemToggleSystem>();
            var wield = entities.System<SharedWieldableSystem>();
            var charges = entities.System<SharedChargesSystem>();
            var counter = entities.GetComponent<LimitedChargesComponent>(weapon);
            var reflector = entities.GetComponent<ReflectComponent>(weapon);
            var grip = entities.GetComponent<WieldableComponent>(weapon);
            Assert.That(reflector.ReflectProb, Is.EqualTo(0.5f));
            reflector.ReflectProb = 1; // Deterministic success; production chance checked above.

            Assert.That(hands.TryPickup(user, weapon), Is.True);
            var firstShot = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(Reflect(firstShot, shooter), Is.False, "An inactive weapon cannot reflect.");
            Assert.That(toggle.TryActivate(weapon, user), Is.True);
            Assert.That(Reflect(firstShot, shooter), Is.False, "One hand cannot reflect.");
            Assert.That(counter.Charges, Is.Zero);

            Assert.That(wield.TryWield(weapon, grip, user), Is.True);
            Assert.That(Reflect(firstShot, shooter), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(1));
            Assert.That(Reflect(firstShot, shooter), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(1), "A ricochet chain cannot supply another charge.");
            Assert.That(Reflect(entities.SpawnEntity(null, map.GridCoords), user), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(1), "Self-fired shots do not charge the weapon.");

            var projectile = entities.SpawnEntity("BulletTaser", map.GridCoords);
            var projectileComp = entities.GetComponent<ProjectileComponent>(projectile);
            projectileComp.Shooter = shooter;
            var projectileAttempt = new ProjectileReflectAttemptEvent(projectile, projectileComp, false);
            entities.EventBus.RaiseLocalEvent(user, ref projectileAttempt);
            Assert.That(projectileAttempt.Cancelled, Is.True);
            Assert.That(counter.Charges, Is.EqualTo(2), "Physical projectiles also charge the weapon.");

            for (var i = 0; i < 3; i++)
                Assert.That(Reflect(entities.SpawnEntity(null, map.GridCoords), shooter), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(3));

            charges.UseCharge(weapon, counter);
            Assert.That(Reflect(firstShot, shooter), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(2));

            var melee = entities.System<SharedMeleeWeaponSystem>();
            Assert.That(HealthDamage(melee.GetDamage(weapon, user)), Is.EqualTo(40));
            Assert.That(wield.TryUnwield(weapon, grip, user), Is.True);
            Assert.That(toggle.IsActivated(weapon), Is.True, "Grip changes must preserve power.");
            Assert.That(HealthDamage(melee.GetDamage(weapon, user)), Is.EqualTo(25));
            Assert.That(counter.Charges, Is.EqualTo(2));
            Assert.That(Reflect(entities.SpawnEntity(null, map.GridCoords), shooter), Is.False);

            Assert.That(toggle.TryDeactivate(weapon, user), Is.True);
            Assert.That(counter.Charges, Is.Zero);
            charges.AddCharges(weapon, 2, counter);
            Assert.That(hands.TryDrop(user, weapon), Is.True);
            Assert.That(counter.Charges, Is.Zero);

            bool Reflect(EntityUid shot, EntityUid from)
            {
                var attempt = new HitScanReflectAttemptEvent(from, weapon, ReflectType.Energy,
                    Vector2.UnitX, false, new DamageSpecifier(), shot);
                entities.EventBus.RaiseLocalEvent(user, ref attempt);
                return attempt.Reflected;
            }
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task FullDischargeHitsOneTargetAndDoesNotSpendOnMissesOrBlocks(int storedCharges)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weapon = entities.SpawnEntity("WeaponEnergyHalberdMARSOC", map.GridCoords);
            var first = entities.SpawnEntity("TargetHuman", map.GridCoords.Offset(new Vector2(1, 0.5f)));
            var second = entities.SpawnEntity("TargetHuman", map.GridCoords.Offset(new Vector2(1, -0.5f)));
            var hands = entities.System<SharedHandsSystem>();
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var chargeSystem = entities.System<SharedChargesSystem>();
            var counter = entities.GetComponent<LimitedChargesComponent>(weapon);
            var meleeComp = entities.GetComponent<MeleeWeaponComponent>(weapon);
            Assert.That(hands.TryPickup(user, weapon), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(weapon, user), Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            chargeSystem.AddCharges(weapon, storedCharges, counter);
            var thunderBefore = CountSound();
            var bladeBefore = CountSound("/Audio/Weapons/eblade1.ogg");

            melee.GetDamage(weapon, user);
            melee.GetDamage(weapon, user);
            Assert.That(counter.Charges, Is.EqualTo(storedCharges));
            meleeComp.NextAttack = TimeSpan.Zero;
            melee.AttemptLightAttackMiss(user, weapon, meleeComp, map.GridCoords);
            Assert.That(counter.Charges, Is.EqualTo(storedCharges));
            Assert.That(CountSound(), Is.EqualTo(thunderBefore));

            meleeComp.NextAttack = TimeSpan.Zero;
            Assert.That(melee.AttemptLightAttack(user, weapon, meleeComp, first), Is.True);
            Assert.That(counter.Charges, Is.Zero);
            var firstDamage = entities.GetComponent<DamageableComponent>(first);
            var bonus = storedCharges * 8;
            Assert.That(firstDamage.Damage.DamageDict["Shock"].Float(), Is.EqualTo(bonus));
            Assert.That(CountSound(), Is.EqualTo(thunderBefore + 1));
            Assert.That(CountSound("/Audio/Weapons/eblade1.ogg"), Is.EqualTo(bladeBefore + 1),
                "The discharge sound must not replace the normal blade impact.");
            var slashBefore = firstDamage.Damage.DamageDict["Slash"].Float();

            chargeSystem.AddCharges(weapon, storedCharges, counter);
            meleeComp.NextAttack = TimeSpan.Zero;
            Assert.That(melee.AttemptHeavyAttack(user, weapon, meleeComp, [first, first, second],
                map.GridCoords.Offset(new Vector2(1.6f, 0))), Is.True);
            Assert.That(counter.Charges, Is.Zero);
            var firstBonus = firstDamage.Damage.DamageDict["Shock"].Float() - bonus;
            var secondBonus = entities.GetComponent<DamageableComponent>(second).Damage.DamageDict["Shock"].Float();
            Assert.That(firstBonus + secondBonus, Is.EqualTo(bonus));
            Assert.That(firstBonus == 0 || secondBonus == 0, Is.True, "A wide swing must discharge into only one target.");
            Assert.That(firstDamage.Damage.DamageDict["Slash"].Float() - slashBefore, Is.EqualTo(15),
                "Duplicate targets must not receive another hit.");
            Assert.That(CountSound(), Is.EqualTo(thunderBefore + 2));

            chargeSystem.AddCharges(weapon, storedCharges, counter);
            entities.AddComponent<GodmodeComponent>(first);
            meleeComp.NextAttack = TimeSpan.Zero;
            Assert.That(melee.AttemptLightAttack(user, weapon, meleeComp, first), Is.True);
            Assert.That(counter.Charges, Is.EqualTo(storedCharges));
            Assert.That(CountSound(), Is.EqualTo(thunderBefore + 2), "Blocked damage must not play a discharge.");

            int CountSound(string path = "/Audio/_Exodus/Weapons/Melee/smite_thunder.ogg")
            {
                var count = 0;
                var query = entities.EntityQueryEnumerator<AudioComponent>();
                while (query.MoveNext(out _, out var audio))
                {
                    if (audio.FileName == path)
                        count++;
                }

                return count;
            }
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PreciseAndWideAttacksReachTwoTiles(bool wide)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weapon = entities.SpawnEntity("WeaponEnergyHalberdMARSOC", map.GridCoords);
            var target = entities.SpawnEntity("TargetHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, weapon), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(weapon, user), Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var weaponComp = entities.GetComponent<MeleeWeaponComponent>(weapon);
            Assert.That(weaponComp.Range, Is.EqualTo(2));
            weaponComp.NextAttack = TimeSpan.Zero;
            var hit = wide
                ? melee.AttemptHeavyAttack(user, weapon, weaponComp, [target], map.GridCoords.Offset(new Vector2(2, 0)))
                : melee.AttemptLightAttack(user, weapon, weaponComp, target);
            Assert.That(hit, Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConfigurableChargeCostWorksWithoutReflectionAndRefundsBlockedDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var weapon = entities.SpawnEntity("ExodusTestChargedCrowbar", map.GridCoords);
            var target = entities.SpawnEntity("TargetHuman", map.GridCoords.Offset(Vector2.UnitX));
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, weapon), Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var meleeComp = entities.GetComponent<MeleeWeaponComponent>(weapon);
            var counter = entities.GetComponent<LimitedChargesComponent>(weapon);
            var damage = entities.GetComponent<DamageableComponent>(target);

            entities.AddComponent<GodmodeComponent>(target);
            Hit();
            Assert.That(damage.TotalDamage.Float(), Is.Zero);
            Assert.That(counter.Charges, Is.EqualTo(5), "A fully blocked hit must not spend charges.");
            entities.RemoveComponent<GodmodeComponent>(target);

            for (var i = 0; i < 2; i++)
            {
                Hit();
                Assert.That(counter.Charges, Is.EqualTo(5 - (i + 1) * 2));
                Assert.That(damage.Damage.DamageDict["Heat"].Float(), Is.EqualTo((i + 1) * 6));
            }

            Hit();
            Assert.That(counter.Charges, Is.EqualTo(1));
            Assert.That(damage.Damage.DamageDict["Heat"].Float(), Is.EqualTo(12), "Insufficient charges cannot add bonus damage.");

            void Hit()
            {
                meleeComp.NextAttack = TimeSpan.Zero;
                Assert.That(melee.AttemptLightAttack(user, weapon, meleeComp, target), Is.True);
            }
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    private static float HealthDamage(DamageSpecifier damage)
    {
        return damage.DamageDict["Slash"].Float() + damage.DamageDict["Heat"].Float();
    }
}
