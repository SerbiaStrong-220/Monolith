#nullable enable
using System.Numerics;
using Content.Server._Exodus.Medical;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._Exodus.Medical;
using Content.Shared._Exodus.Visuals;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
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
    public async Task TreatmentUsesGroupBudgetsAndClotsWithoutReplacingBlood()
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            var damage = entities.GetComponent<DamageableComponent>(target);
            var blood = entities.GetComponent<BloodstreamComponent>(target);
            var bloodstream = entities.System<BloodstreamSystem>();
            var bleed = blood.BleedAmount;
            var bloodLevel = bloodstream.GetBloodLevelPercentage(target, blood);
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
            Assert.That(blood.BleedAmount, Is.EqualTo(Math.Max(0f, bleed - 0.2f)).Within(0.00001f));
            Assert.That(bloodstream.GetBloodLevelPercentage(target, blood), Is.EqualTo(bloodLevel));
            Assert.That(battery.CurrentCharge, Is.EqualTo(initialCharge - 7.2f).Within(0.001f));

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

    [TestCase(MedicalBeamMode.Manual)]
    [TestCase(MedicalBeamMode.Automatic)]
    public async Task HealthyPatientsKeepLoopAndConsumeCharge(MedicalBeamMode mode)
    {
        await WithPatient((entities, user, gun, target, _, now) =>
        {
            var damage = entities.GetComponent<DamageableComponent>(target);
            var damageSystem = entities.System<DamageableSystem>();
            damageSystem.SetAllDamage(target, damage, FixedPoint2.Zero);

            Assert.That(entities.System<PowerCellSystem>().TryGetBatteryFromSlot(gun, out var battery), Is.True);
            var charge = battery!.CurrentCharge;
            var initialDamage = damage.TotalDamage;
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetMode((gun, config), user, mode), Is.True);
            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            system.Update(0f);
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            Assert.That(active.AudioStream, Is.Not.Null, "Even a healthy target must start the loop immediately.");
            var stream = active.AudioStream;

            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
            Assert.That(active.AudioStream, Is.EqualTo(stream));
            Assert.That(battery.CurrentCharge, Is.EqualTo(charge - 7.2f).Within(0.001f));
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage));

            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);
            Assert.That(active.AudioStream, Is.EqualTo(stream));
            Assert.That(battery.CurrentCharge, Is.EqualTo(charge - 14.4f).Within(0.001f));
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
                    Assert.That(entities.GetComponent<TransformComponent>(wall).Anchored, Is.True);
                    break;
                case "range":
                    entities.System<SharedTransformSystem>().SetCoordinates(target,
                        new EntityCoordinates(grid, new Vector2(10.6f, 0.5f)));
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
            Assert.That(active.Running, Is.False);
            Assert.That(active.AudioStream, Is.Null);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.Null);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage, Is.EqualTo(damage));
        });
    }

    [Test]
    public async Task AutomaticModeTracksWithoutHeldInputAndUsesManualChargeRate()
    {
        await WithPatient((entities, user, gun, target, _, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetMode((gun, config), user, MedicalBeamMode.Automatic), Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, false);
            Assert.That(system.TryHandleInput((gun, config), user, target, MedicalBeamMode.Automatic), Is.True);
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            active.InputExpires = now;
            active.NextHeal = now;
            Assert.That(entities.System<PowerCellSystem>().TryGetBatteryFromSlot(gun, out var battery), Is.True);
            var charge = battery!.CurrentCharge;
            system.Update(0f);

            var damage = entities.GetComponent<DamageableComponent>(target);
            Assert.That(damage.DamagePerGroup["Brute"], Is.EqualTo(FixedPoint2.New(19.6)));
            Assert.That(damage.DamagePerGroup["Burn"], Is.EqualTo(FixedPoint2.New(19.6)));
            Assert.That(damage.Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.New(9.6)));
            Assert.That(battery.CurrentCharge, Is.EqualTo(charge - 7.2f).Within(0.001f));
            Assert.That(active.AudioStream, Is.Not.Null);
            var stream = active.AudioStream;

            // Releasing manual input after a mode change must not disconnect an automatic beam.
            Assert.That(system.TryHandleInput((gun, config), user, null, MedicalBeamMode.Manual), Is.False);
            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);
            Assert.That(active.Running, Is.True);
            Assert.That(active.AudioStream, Is.EqualTo(stream), "The loop must not restart on each healing pulse.");
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
        });
    }

    [TestCase(MedicalBeamMode.Manual)]
    [TestCase(MedicalBeamMode.Automatic)]
    public async Task OnlyAutomaticModeKeepsTreatingInTheOtherHand(MedicalBeamMode mode)
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetMode((gun, config), user, mode), Is.True);
            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            system.Update(0f);
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            var stream = active.AudioStream;
            var hands = entities.GetComponent<HandsComponent>(user);
            var handsSystem = entities.System<SharedHandsSystem>();
            string? otherHand = null;
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.Name != hands.ActiveHand?.Name)
                {
                    otherHand = hand.Name;
                    break;
                }
            }
            Assert.That(otherHand, Is.Not.Null);
            Assert.That(handsSystem.TrySetActiveHand(user, otherHand), Is.True);
            var tool = entities.SpawnEntity("Screwdriver", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            Assert.That(handsSystem.TryPickup(user, tool), Is.True);
            Assert.That(hands.ActiveHandEntity, Is.EqualTo(tool));
            Assert.That(system.TryHandleInput((gun, config), user, null, mode), Is.False,
                "Input from another active item must not control this medigun.");

            var damage = entities.GetComponent<DamageableComponent>(target);
            var initialDamage = damage.TotalDamage;
            active.NextCheck = now;
            active.NextHeal = now;
            system.Update(0f);
            if (mode == MedicalBeamMode.Manual)
            {
                Assert.That(active.Running, Is.False);
                Assert.That(active.AudioStream, Is.Null);
                Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage));
                return;
            }

            Assert.That(active.Running, Is.True);
            Assert.That(active.AudioStream, Is.EqualTo(stream));
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage - FixedPoint2.New(1.2)));
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
            Assert.That(handsSystem.TryDrop(user, gun), Is.True);
            Assert.That(active.Running, Is.False);
            Assert.That(active.AudioStream, Is.Null);
        });
    }

    [TestCase(MedicalBeamMode.Manual, 1f, 0.8f)]
    [TestCase(MedicalBeamMode.Automatic, 1f, 0.9333333f)]
    public async Task ClottingWorksWithoutBruteBurnOrAsphyxiationAndDoesNotReplaceBlood(
        MedicalBeamMode mode, float initialBleed, float expectedBleed)
    {
        await WithPatient((entities, user, gun, target, _, now) =>
        {
            var damageSystem = entities.System<DamageableSystem>();
            var damage = entities.GetComponent<DamageableComponent>(target);
            damageSystem.SetAllDamage(target, damage, FixedPoint2.Zero);
            damageSystem.TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Bloodloss"] = 5 } },
                ignoreResistances: true, ignoreGlobalModifiers: true, canSever: false);
            var bloodstream = entities.System<BloodstreamSystem>();
            var blood = entities.GetComponent<BloodstreamComponent>(target);
            Assert.That(bloodstream.TryModifyBloodLevel(target, -20, blood), Is.True);
            Assert.That(bloodstream.TryModifyBleedAmount(target, initialBleed - blood.BleedAmount, blood), Is.True);
            var level = bloodstream.GetBloodLevelPercentage(target, blood);
            Assert.That(level, Is.LessThan(1f));

            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetMode((gun, config), user, mode), Is.True);
            Assert.That(system.TrySetTarget((gun, config), user, target), Is.True);
            entities.GetComponent<MedicalBeamActiveComponent>(gun).NextHeal = now;
            system.Update(0f);

            Assert.That(blood.BleedAmount, Is.EqualTo(expectedBleed).Within(0.00001f));
            Assert.That(bloodstream.GetBloodLevelPercentage(target, blood), Is.EqualTo(level));
            Assert.That(damage.Damage.DamageDict["Bloodloss"], Is.EqualTo(FixedPoint2.New(5)));
        });
    }

    [TestCase("clickAgain")]
    [TestCase("emptyClick")]
    [TestCase("modeSwitch")]
    public async Task AutomaticSelectionCanBeCancelled(string interruption)
    {
        await WithPatient((entities, user, gun, target, _, now) =>
        {
            var system = entities.System<MedicalBeamGunSystem>();
            var config = entities.GetComponent<MedicalBeamGunComponent>(gun);
            Assert.That(system.TrySetMode((gun, config), user, MedicalBeamMode.Automatic), Is.True);
            Assert.That(system.TryHandleInput((gun, config), user, target, MedicalBeamMode.Automatic), Is.True);
            var active = entities.GetComponent<MedicalBeamActiveComponent>(gun);
            active.NextHeal = now;
            system.Update(0f);
            Assert.That(active.AudioStream, Is.Not.Null);

            switch (interruption)
            {
                case "clickAgain":
                    Assert.That(system.TryHandleInput((gun, config), user, target, MedicalBeamMode.Automatic), Is.True);
                    break;
                case "emptyClick":
                    Assert.That(system.TryHandleInput((gun, config), user, null, MedicalBeamMode.Automatic), Is.True);
                    break;
                case "modeSwitch":
                    Assert.That(system.TrySetMode((gun, config), user, MedicalBeamMode.Manual), Is.True);
                    Assert.That(system.TryHandleInput((gun, config), user, target, MedicalBeamMode.Automatic), Is.False);
                    break;
            }

            active.NextCheck = now;
            system.Update(0f);
            Assert.That(active.Running, Is.False);
            Assert.That(active.AudioStream, Is.Null);
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.Null);
        });
    }

    [TestCase("Grille")]
    [TestCase("Window")]
    [TestCase("ReinforcedWindow")]
    public async Task LaserTransparentObstaclesAllowTreatmentAtTenTiles(string obstacle)
    {
        await WithPatient((entities, user, gun, target, grid, now) =>
        {
            var transform = entities.System<SharedTransformSystem>();
            transform.SetCoordinates(target, new EntityCoordinates(grid, new Vector2(10.5f, 0.5f)));
            var structure = entities.SpawnEntity(obstacle, new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            Assert.That(entities.GetComponent<TransformComponent>(structure).Anchored, Is.True);
            var system = entities.System<MedicalBeamGunSystem>();
            Assert.That(system.TrySetTarget((gun, entities.GetComponent<MedicalBeamGunComponent>(gun)), user, target), Is.True);
            entities.GetComponent<MedicalBeamActiveComponent>(gun).NextHeal = now;
            var damage = entities.GetComponent<DamageableComponent>(target);
            var initialDamage = damage.TotalDamage;
            system.Update(0f);
            Assert.That(damage.TotalDamage, Is.EqualTo(initialDamage - FixedPoint2.New(3)));
            Assert.That(entities.GetComponent<EntityLinkVisualComponent>(gun).Target, Is.EqualTo(target));
        });
    }

    [TestCase("PowerCellSmall", true)]
    [TestCase("PowerCellAntiqueProto", true)]
    [TestCase("PowerCageSmall", false)]
    [TestCase("PowerCageMech", false)]
    public async Task OnlyPocketCellsFit(string prototype, bool allowed)
    {
        await WithPatient((entities, user, gun, _, grid, _) =>
        {
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(gun, "cell_slot", null, out _), Is.True);
            var cell = entities.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            Assert.That(slots.TryInsert(gun, "cell_slot", cell, null), Is.EqualTo(allowed));
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
                for (var x = 0; x < 14; x++)
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
