using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Adminbus.Tower7G;
using Content.Server.NPC.HTN;
using Content.Shared._Exodus.Adminbus.Tower7G;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class Tower7GDamageConversionTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: damageModifierSet
          id: Exodus7GTestResistance
          coefficients:
            Piercing: 0.25
            Slash: 0
            Structural: 0
            Radiation: 0
            Cellular: 0
        - type: entity
          id: Exodus7GTestTarget
          parent: TargetHuman
          components:
          - type: Damageable
            damageModifierSet: Exodus7GTestResistance
        """;

    [TestCase("MobRotHungry", 20, 20)]
    [TestCase("MobRotSated", 35, 65)]
    public async Task TowerConvertsNaturalPoisonDamageWithoutChangingBaseDamage(string prototype, int poison, int piercing)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;
        DamageSpecifier original = new();
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity(prototype, map.GridCoords);
            entities.RemoveComponent<HTNComponent>(mob);
            original = new DamageSpecifier(entities.GetComponent<MeleeWeaponComponent>(mob).Damage);
            Assert.That(original.DamageDict["Poison"].Float(), Is.EqualTo(poison));
            var tower = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<Tower7GComponent>(tower);
        });

        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            var damage = entities.System<SharedMeleeWeaponSystem>().GetDamage(mob, mob);
            var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
            Assert.That(damage.DamageDict.GetValueOrDefault("Poison").Float(), Is.Zero);
            Assert.That(damage.DamageDict.GetValueOrDefault("Piercing"), Is.EqualTo(FixedPoint2.New(piercing) * multiplier));
            foreach (var (type, amount) in original.DamageDict)
            {
                if (type is "Poison" or "Piercing")
                    continue;

                Assert.That(damage.DamageDict.GetValueOrDefault(type), Is.EqualTo(amount * multiplier), type);
            }

            Assert.That(entities.GetComponent<MeleeWeaponComponent>(mob).Damage, Is.EqualTo(original));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase("MobRotNester")]
    [TestCase("MobRotSpawn")]
    public async Task NonPoisonousRotKeepsItsNaturalDamage(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            SpawnTower(entities, map.GridCoords);
            var mob = entities.SpawnEntity(prototype, map.GridCoords);
            entities.RemoveComponent<HTNComponent>(mob);
            var original = new DamageSpecifier(entities.GetComponent<MeleeWeaponComponent>(mob).Damage);
            Assert.That(original.DamageDict.GetValueOrDefault("Poison"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(entities.GetComponent<Tower7GDamageConversionComponent>(mob).Active, Is.True);
            var damage = entities.System<SharedMeleeWeaponSystem>().GetDamage(mob, mob);
            var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
            Assert.That(damage, Is.EqualTo(original * multiplier));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(mob).Damage, Is.EqualTo(original));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoverageUsesFiveSecondPulsesAcrossGridsAndSurvivesOverlappingTowerDeletion()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        EntityUid mob = default;
        EntityUid firstTower = default;
        EntityUid secondTower = default;
        EntityCoordinates covered = default;
        var outside = new EntityCoordinates(map.MapUid, 24, 0);
        await server.WaitAssertion(() =>
        {
            var grid = server.MapMan.CreateGridEntity(map.MapId);
            entities.System<SharedMapSystem>().SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            entities.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(8, 0));
            covered = new EntityCoordinates(grid, Vector2.Zero);
            firstTower = SpawnTower(entities, map.GridCoords, 10);
            secondTower = SpawnTower(entities, covered, 10);
            mob = SpawnHungry(entities, outside);
            AssertNaturalDamage(entities, mob, 20, 0);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, covered);
        });

        // Observe a pulse instead of depending on the pooled server's timer phase.
        var converted = false;
        for (var i = 0; i < 51 && !converted; i++)
        {
            await pair.RunSeconds(.1f);
            await server.WaitAssertion(() => converted = entities.System<SharedMeleeWeaponSystem>()
                .GetDamage(mob, mob).DamageDict.GetValueOrDefault("Poison") == 0);
        }

        Assert.That(converted, Is.True, "Entering the field must convert damage within five seconds.");
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, outside);
        });
        await pair.RunSeconds(4.7f);
        await server.WaitAssertion(() => AssertNaturalDamage(entities, mob, 0, 20));
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 20, 0);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, otherMap.GridCoords);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 20, 0);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, covered);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.DeleteEntity(secondTower);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.DeleteEntity(firstTower);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() => AssertNaturalDamage(entities, mob, 20, 0));
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            maps.DeleteMap(map.MapId);
            maps.DeleteMap(otherMap.MapId);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientDamageFollowsTheFieldWhileExternalWeaponDamageStaysUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;
        EntityUid tower = default;
        EntityUid weapon = default;
        NetEntity towerNet = default;
        var previousPvs = server.CfgMan.GetCVar(CVars.NetPVS);
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            mob = SpawnHungry(entities, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            weapon = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<MeleeWeaponComponent>(weapon).Damage = new DamageSpecifier
            {
                DamageDict = new() { { "Poison", 9 }, { "Piercing", 4 } },
            };
        });
        await pair.RunUntilSynced();
        await pair.Client.WaitAssertion(() => AssertNaturalDamage(pair.Client.EntMan, pair.ToClientUid(mob), 20, 0));
        await server.WaitAssertion(() =>
        {
            tower = SpawnTower(entities, new EntityCoordinates(map.MapUid, 100, 0));
            towerNet = entities.GetNetEntity(tower);
        });
        await pair.RunSeconds(5.1f);
        await pair.RunUntilSynced();
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            var damage = entities.System<SharedMeleeWeaponSystem>().GetDamage(weapon, mob);
            var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
            Assert.That(damage.DamageDict["Poison"], Is.EqualTo(FixedPoint2.New(9) * multiplier));
            Assert.That(damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(4) * multiplier));
        });
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(pair.Client.EntMan.TryGetEntity(towerNet, out _), Is.False,
                "The client must calculate converted damage without receiving the distant tower.");
            AssertNaturalDamage(pair.Client.EntMan, pair.ToClientUid(mob), 0, 20);
        });
        await server.WaitPost(() => entities.DeleteEntity(tower));
        await pair.RunSeconds(5.1f);
        await pair.RunUntilSynced();
        await pair.Client.WaitAssertion(() => AssertNaturalDamage(pair.Client.EntMan, pair.ToClientUid(mob), 20, 0));
        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.System<SharedMapSystem>().DeleteMap(map.MapId);
            server.CfgMan.SetCVar(CVars.NetPVS, previousPvs);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExistingHeatTargetsStillReceiveDistanceScaledHeat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid target = default;
        EntityUid unmarked = default;
        await server.WaitAssertion(() =>
        {
            SpawnTower(entities, map.GridCoords, 10);
            target = SpawnHungry(entities, new EntityCoordinates(map.MapUid, 5, 0));
            entities.AddComponent<Tower7GTargetComponent>(target);
            unmarked = SpawnHungry(entities, new EntityCoordinates(map.MapUid, 0, 5));
        });

        var heated = false;
        for (var i = 0; i < 51 && !heated; i++)
        {
            await pair.RunSeconds(.1f);
            await server.WaitAssertion(() => heated = entities.GetComponent<DamageableComponent>(target)
                .Damage.DamageDict.GetValueOrDefault("Heat") > 0);
        }

        await server.WaitAssertion(() =>
        {
            var multiplier = entities.System<DamageableSystem>().UniversalAllDamageModifier;
            Assert.That(entities.GetComponent<DamageableComponent>(target).Damage.DamageDict["Heat"],
                Is.EqualTo(FixedPoint2.New(27.5f) * multiplier));
            Assert.That(entities.GetComponent<DamageableComponent>(unmarked).Damage.DamageDict["Heat"].Float(), Is.Zero);
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase(7, "Cold", 0, 9)]
    [TestCase(0, "Cold", 0, 2)]
    [TestCase(-4, "Cold", -4, 2)]
    [TestCase(7, "Heat", 7, 2)]
    public async Task ConfiguredConversionPreservesNonpositiveDamageAndRepeatedReads(int source, string targetType, int heat, int cold)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity(null, map.GridCoords);
            var original = new DamageSpecifier
            {
                DamageDict = new() { { "Heat", source }, { "Cold", 2 }, { "Blunt", 3 } },
            };
            var weapon = entities.AddComponent<MeleeWeaponComponent>(mob);
            weapon.Damage = new DamageSpecifier(original);
            var conversion = entities.AddComponent<Tower7GDamageConversionComponent>(mob);
            conversion.SourceType = "Heat";
            conversion.TargetType = targetType;
            conversion.Active = true;
            var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
            for (var i = 0; i < 2; i++)
            {
                var damage = entities.System<SharedMeleeWeaponSystem>().GetDamage(mob, mob);
                Assert.That(damage.DamageDict.GetValueOrDefault("Heat"), Is.EqualTo(FixedPoint2.New(heat) * multiplier));
                Assert.That(damage.DamageDict.GetValueOrDefault("Cold"), Is.EqualTo(FixedPoint2.New(cold) * multiplier));
                Assert.That(damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(3) * multiplier));
                Assert.That(weapon.Damage, Is.EqualTo(original));
            }
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualHitsApplyPiercingResistanceAfterConversion(bool wide)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;
        EntityUid target = default;
        var targetCoordinates = map.GridCoords.Offset(Vector2.UnitX);
        await server.WaitAssertion(() =>
        {
            SpawnTower(entities, map.GridCoords);
            mob = SpawnHungry(entities, map.GridCoords);
            target = entities.SpawnEntity("Exodus7GTestTarget", targetCoordinates);
        });
        await pair.RunSeconds(1.1f);
        await server.WaitAssertion(() =>
        {
            entities.System<SharedCombatModeSystem>().SetInCombatMode(mob, true);
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var weapon = entities.GetComponent<MeleeWeaponComponent>(mob);
            weapon.NextAttack = TimeSpan.Zero;
            var hit = wide
                ? melee.AttemptHeavyAttack(mob, mob, weapon, [target], targetCoordinates)
                : melee.AttemptLightAttack(mob, mob, weapon, target);
            Assert.That(hit, Is.True);
            var modifiers = entities.System<DamageableSystem>();
            var expected = FixedPoint2.New(20) * modifiers.UniversalMeleeDamageModifier * .25f * modifiers.UniversalAllDamageModifier;
            var damage = entities.GetComponent<DamageableComponent>(target);
            Assert.That(damage.Damage.DamageDict["Piercing"], Is.EqualTo(expected));
            Assert.That(damage.TotalDamage, Is.EqualTo(expected));
        });
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoverageRejectsInvalidRadiiAndRefreshesAfterMapUnpauseAndSourceRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid tower = default;
        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            tower = SpawnTower(entities, map.GridCoords, 10);
            var source = entities.GetComponent<Tower7GComponent>(tower);
            foreach (var range in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                source.Range = range;
                var invalid = SpawnHungry(entities, map.GridCoords);
                AssertNaturalDamage(entities, invalid, 20, 0);
                entities.DeleteEntity(invalid);
            }

            source.Range = 10;
            mob = SpawnHungry(entities, new EntityCoordinates(map.MapUid, 10, 0));
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, new EntityCoordinates(map.MapUid, 10.01f, 0));
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 20, 0);
            entities.System<SharedTransformSystem>().SetCoordinates(mob, new EntityCoordinates(map.MapUid, 10, 0));
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.System<SharedMapSystem>().SetPaused(map.MapId, true);
            entities.RemoveComponent<Tower7GComponent>(tower);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() =>
        {
            AssertNaturalDamage(entities, mob, 0, 20);
            entities.System<SharedMapSystem>().SetPaused(map.MapId, false);
        });
        await pair.RunSeconds(5.1f);
        await server.WaitAssertion(() => AssertNaturalDamage(entities, mob, 20, 0));
        await server.WaitPost(() => entities.System<SharedMapSystem>().DeleteMap(map.MapId));
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnTower(IEntityManager entities, EntityCoordinates coordinates, float range = 512)
    {
        var tower = entities.SpawnEntity(null, coordinates);
        entities.AddComponent<Tower7GComponent>(tower).Range = range;
        return tower;
    }

    private static EntityUid SpawnHungry(IEntityManager entities, EntityCoordinates coordinates)
    {
        var mob = entities.SpawnEntity("MobRotHungry", coordinates);
        entities.RemoveComponent<HTNComponent>(mob);
        return mob;
    }

    private static void AssertNaturalDamage(IEntityManager entities, EntityUid mob, int poison, int piercing)
    {
        var damage = entities.System<SharedMeleeWeaponSystem>().GetDamage(mob, mob);
        var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
        Assert.That(damage.DamageDict.GetValueOrDefault("Poison"), Is.EqualTo(FixedPoint2.New(poison) * multiplier));
        Assert.That(damage.DamageDict.GetValueOrDefault("Piercing"), Is.EqualTo(FixedPoint2.New(piercing) * multiplier));
    }
}
