using System.Numerics;
using Content.Server._Exodus.Medical;
using Content.Server.Body.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._Exodus.Medical;
using Content.Shared._Exodus.Visuals;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(MedicalBeamGunSystem))]
public sealed class MedicalBeamGunTest
{
    [Test]
    public async Task TreatmentUsesGroupBudgetsAndLeavesBloodUntouched()
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            var damage = entities.GetComponent<DamageableComponent>(target);
            var blood = entities.GetComponent<BloodstreamComponent>(target);
            var bleed = blood.BleedAmount;
            var cells = entities.System<PowerCellSystem>();
            Assert.That(cells.TryGetBatteryFromSlot(gun, out var battery), Is.True);
            var initialCharge = battery!.CurrentCharge;
            var initialDamage = damage.TotalDamage;

            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            system.Update(0f);
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            Assert.That(active.NextHeal, Is.GreaterThan(now), "Clicking must not immediately grant a free heal.");
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage));
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));

            var deadline = active.NextHeal;
            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            Assert.That(active.NextHeal, Is.EqualTo(deadline), "Input heartbeats must preserve the treatment schedule.");
            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);

            Assert.That(damage.DamagePerGroup["Brute"], Is.EqualTo(FixedPoint2.New(19)));
            Assert.That(damage.DamagePerGroup["Burn"], Is.EqualTo(FixedPoint2.New(19)));
            Assert.That(damage.Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.New(9)));
            foreach (var type in new[] { "Bloodloss", "Poison", "Radiation", "Cellular" })
                Assert.That(damage.Damage.DamageDict[type], Is.EqualTo(FixedPoint2.New(5)), type);
            Assert.That(blood.BleedAmount, Is.EqualTo(bleed));
            Assert.That(battery.CurrentCharge, Is.EqualTo(initialCharge - 2.4f).Within(0.001f));

            var otherUser = entities.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 1.5f)));
            var otherGun = entities.SpawnEntity("MedicalBeamGun", new EntityCoordinates(grid, new Vector2(0.5f, 1.5f)));
            entities.System<SharedCombatModeSystem>().SetInCombatMode(otherUser, true);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(otherUser, otherGun), Is.True);
            Assert.That(system.TrySetTarget((otherGun, entities.GetComponent<MedicalBeamGunComponent>(otherGun)), otherUser, target), Is.True);
            var otherActive = entities.GetComponent<MedicalBeamActiveComponent>(otherGun);
            otherActive.NextHeal = now;
            var treatedDamage = damage.TotalDamage;
            system.Update(0f);
            Assert.That(damage.TotalDamage, Is.EqualTo(treatedDamage), "A second medigun must not add healing.");
            Assert.That(cells.TryGetBatteryFromSlot(otherGun, out var otherBattery), Is.True);
            Assert.That(otherBattery!.CurrentCharge, Is.EqualTo(initialCharge));

            Assert.That(entities.System<SharedHandsSystem>().TryDrop(user, gun), Is.True);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.Null);
            otherActive.NextCheck = now;
            system.Update(0f);
            Assert.That(damage.TotalDamage, Is.EqualTo(treatedDamage), "Changing medics cannot bypass the patient's cooldown.");
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(otherGun).Target, Is.EqualTo(target));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NoTreatableInjuriesDoNotConsumeCharge(bool excludedInjury)
    {
        await WithPatient((entities, user, gun, target, _, now) =>
        {
            var damage = entities.GetComponent<DamageableComponent>(target);
            var damageSystem = entities.System<DamageableSystem>();
            damageSystem.SetAllDamage(target, damage, FixedPoint2.Zero);
            if (excludedInjury)
            {
                damageSystem.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Poison"] = 5 } },
                    ignoreResistances: true, ignoreGlobalModifiers: true, canSever: false);
            }

            Assert.That(entities.System<PowerCellSystem>().TryGetBatteryFromSlot(gun, out var battery), Is.True);
            var charge = battery!.CurrentCharge;
            var initialDamage = damage.TotalDamage;
            var system = entities.System<MedicalBeamGunSystem>();
            Assert.That(system.TrySetTarget((gun, entities.GetComponent<MedicalBeamGunComponent>(gun)), user, target), Is.True);
            entities.GetComponent<MedicalBeamActiveComponent>(gun).NextHeal = now;
            system.Update(0f);

            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
            Assert.That(battery.CurrentCharge, Is.EqualTo(charge));
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage));
        });
    }

    [Test]
    public async Task CriticalPatientCanBeTreatedWhileMedicMovesAndTakesDamage()
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetTarget((gun, config), user, user), Is.False);
            entities.System<MobStateSystem>().ChangeMobState(target, MobState.Critical);
            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            system.Update(0f);

            entities.System<SharedTransformSystem>().SetCoordinates(user,
                new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            entities.System<DamageableSystem>().TryChangeDamage(user,
                new DamageSpecifier { DamageDict = { ["Piercing"] = 10 } },
                ignoreResistances: true, ignoreGlobalModifiers: true, canSever: false);
            var damage = entities.GetComponent<DamageableComponent>(target);
            var initialDamage = damage.TotalDamage;
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);

            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage - FixedPoint2.New(3)));
        });
    }

    [TestCase("wall")]
    [TestCase("range")]
    [TestCase("timeout")]
    [TestCase("emptyCell")]
    [TestCase("dead")]
    [TestCase("combatMode")]
    public async Task InvalidChannelStopsWithoutHealing(string interruption)
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            Assert.That(system.TrySetTarget((gun, entities.GetComponent<MedicalBeamGunComponent>(gun)), user, target), Is.True);
            system.Update(0f);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            active.NextCheck = now;
            active.NextHeal = now;

            switch (interruption)
            {
                case "wall":
                    var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
                    entities.System<SharedTransformSystem>().AnchorEntity(wall);
                    break;
                case "range":
                    entities.System<SharedTransformSystem>().SetCoordinates(target,
                        new EntityCoordinates(grid, new Vector2(8.5f, 0.5f)));
                    break;
                case "timeout":
                    active.InputExpires = now;
                    break;
                case "emptyCell":
                    Assert.That(entities.System<PowerCellSystem>().TryGetBatteryFromSlot(gun, out var cell, out var battery), Is.True);
                    entities.System<BatterySystem>().SetCharge(cell!.Value, 0, battery);
                    break;
                case "dead":
                    entities.System<MobStateSystem>().ChangeMobState(target, MobState.Dead);
                    break;
                case "combatMode":
                    entities.System<SharedCombatModeSystem>().SetInCombatMode(user, false);
                    break;
            }

            var damage = entities.GetComponent<DamageableComponent>(target).TotalDamage;
            system.Update(0f);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.Null);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage, Is.EqualTo(damage));
        });
    }

    private static async Task WithPatient(Action<IEntityManager, EntityUid, EntityUid, EntityUid, EntityUid, TimeSpan> test)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = 0; x < 10; x++)
                for (var y = 0; y < 3; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(1));
                var user = entities.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                var target = entities.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));
                var gun = entities.SpawnEntity("MedicalBeamGun", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
                Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
                entities.System<DamageableSystem>().TryChangeDamage(target, new DamageSpecifier
                {
                    DamageDict = { ["Slash"] = 5, ["Piercing"] = 15, ["Heat"] = 20, ["Asphyxiation"] = 10,
                        ["Bloodloss"] = 5, ["Poison"] = 5, ["Radiation"] = 5, ["Cellular"] = 5 },
                }, ignoreResistances: true, ignoreGlobalModifiers: true, canSever: false);
                test(entities, user, gun, target, grid, server.ResolveDependency<IGameTiming>().CurTime);
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
}
