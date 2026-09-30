using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._EinsteinEngines.Language;
using Content.Server._Exodus.Stances;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Body;
using Content.Shared._Exodus.Stances;
using Content.Shared._Exodus.Territory;
using Content.Shared._White.Overlays;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Overlays;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class SpecialCreatureTest
{
    [Test]
    public async Task RunningReleasesPilotAndPullWhileKeepingEquippedBag()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid creature = default;
        EntityUid load = default;
        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            creature = em.SpawnEntity("MobSpecial", new EntityCoordinates(map.Grid, .5f, .5f));
            load = em.SpawnEntity("MobSpecial", new EntityCoordinates(map.Grid, 1f, .5f));
            human = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, .5f, 1f));
            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Upright));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var pulling = em.System<PullingSystem>();
            Assert.That(pulling.TryStartPull(human, load));
            Assert.That(em.GetComponent<MovementSpeedModifierComponent>(human).WalkSpeedModifier, Is.LessThanOrEqualTo(.21f));
            Assert.That(pulling.TryStopPull(load, em.GetComponent<PullableComponent>(load)));
            Assert.That(pulling.TryStartPull(creature, load));
            Assert.That(em.GetComponent<MovementSpeedModifierComponent>(creature).WalkSpeedModifier, Is.EqualTo(1f).Within(.001));

            var bag = em.SpawnEntity("ClothingBackpackSatchel", new EntityCoordinates(map.Grid, .5f, .5f));
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(creature, bag));
            Assert.That(em.System<InventorySystem>().TryEquip(creature, bag, "back"));
            var console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid, .5f, .5f));
            var helm = em.GetComponent<ShuttleConsoleComponent>(console);
            em.EnsureComponent<PilotComponent>(creature);
            em.System<ShuttleConsoleSystem>().AddPilot(console, creature, helm);
            Assert.That(em.GetComponent<PilotComponent>(creature).Console, Is.EqualTo(console));
            var inputs = new GetShuttleInputsEvent(.1f, map.Grid);
            em.EventBus.RaiseLocalEvent(creature, ref inputs);
            Assert.That(inputs.AngularMul, Is.EqualTo(1.3f));
            Assert.That(inputs.AccelMul, Is.EqualTo(1.3f));

            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Quadruped));
            Assert.That(em.HasComponent<PilotComponent>(creature), Is.False);
            Assert.That(helm.SubscribedPilots, Does.Not.Contain(creature));
            Assert.That(em.GetComponent<PullerComponent>(creature).Pulling, Is.Null);
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(creature, "back", out var equipped));
            Assert.That(equipped, Is.EqualTo(bag));
            Assert.That(em.GetComponent<HandsComponent>(creature).Hands, Is.Empty);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var blocker = em.System<ActionBlockerSystem>();
            Assert.That(blocker.CanAttack(creature));
            Assert.That(blocker.CanInteract(creature, null), Is.False);
            Assert.That(em.GetComponent<MovementSpeedModifierComponent>(creature).CurrentSprintSpeed, Is.EqualTo(6.6f).Within(.01));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CriticalRegenerationRecoversInThinAirAndRemovedOrgansLoseAbilities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid map = default;
        EntityUid creature = default;
        await server.WaitAssertion(() =>
        {
            map = em.System<SharedMapSystem>().CreateMap(out var mapId);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                new ResPath("/Maps/_Exodus/POI/special_wreck.yml"), out var grid));
            creature = em.SpawnEntity("MobSpecial", new EntityCoordinates(grid!.Value.Owner, 5.5f, 5.5f));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Piercing", FixedPoint2.New(705));
            em.System<DamageableSystem>().TryChangeDamage(creature, damage, ignoreResistances: true);
            Assert.That(em.GetComponent<MobStateComponent>(creature).CurrentState, Is.EqualTo(MobState.Critical));
        });
        await pair.RunSeconds(12);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MobStateComponent>(creature).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(em.GetComponent<PressureBreathingComponent>(creature).CanBreathe);
            Assert.That(em.System<AtmosphereSystem>().GetContainingMixture(creature)!.Pressure, Is.GreaterThan(20));
            var body = em.System<SharedBodySystem>();
            var organs = body.GetBodyOrganEntityComps<OrganComponent>((creature, null));
            var eyes = organs.Single(o => em.GetComponent<MetaDataComponent>(o.Owner).EntityPrototype!.ID == "OrganSpecialEyes");
            var lungs = organs.Single(o => em.GetComponent<MetaDataComponent>(o.Owner).EntityPrototype!.ID == "OrganSpecialLungs");
            Assert.That(body.RemoveOrgan(eyes.Owner));
            Assert.That(body.RemoveOrgan(lungs.Owner));
        });
        await pair.RunSeconds(.1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<NightVisionComponent>(creature), Is.False);
            Assert.That(em.HasComponent<ThermalVisionComponent>(creature), Is.False);
            Assert.That(em.HasComponent<PressureBreathingComponent>(creature), Is.False);
            em.DeleteEntity(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WreckContainsExactlyTwoCompleteTimeLockedSurvivors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var mapUid = em.System<SharedMapSystem>().CreateMap(out var mapId);
            try
            {
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                    new ResPath("/Maps/_Exodus/POI/special_wreck.yml"), out var grid));
                var survivors = em.EntityQueryEnumerator<LocomotionStanceComponent, TransformComponent>();
                var count = 0;
                var body = em.System<SharedBodySystem>();
                var pricing = em.System<PricingSystem>();
                while (survivors.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid != grid!.Value.Owner)
                        continue;

                    count++;
                    var ghost = em.GetComponent<GhostRoleComponent>(uid);
                    Assert.That(ghost.Requirements!.OfType<OverallPlaytimeRequirement>().Single().Time,
                        Is.EqualTo(TimeSpan.FromHours(250)));
                    Assert.That(body.GetBodyChildren(uid).Count(), Is.EqualTo(11));
                    var organs = body.GetBodyOrganEntityComps<OrganComponent>((uid, null));
                    Assert.That(organs.Count, Is.EqualTo(7));
                    foreach (var (partId, _) in body.GetBodyChildren(uid))
                    {
                        Assert.That(em.GetComponent<StaticPriceComponent>(partId).Price, Is.EqualTo(90000));
                        Assert.That(pricing.GetPrice(partId, includeContents: false), Is.GreaterThanOrEqualTo(90000));
                    }
                    foreach (var organ in organs)
                    {
                        Assert.That(em.GetComponent<StaticPriceComponent>(organ.Owner).Price, Is.EqualTo(90000));
                        Assert.That(pricing.GetPrice(organ.Owner, includeContents: false), Is.GreaterThanOrEqualTo(90000));
                    }

                    var sample = organs.First().Owner;
                    var beforeReagents = pricing.GetPrice(sample, includeContents: false);
                    var solutions = em.System<SharedSolutionContainerSystem>();
                    Assert.That(solutions.EnsureSolutionEntity((sample, null), "appraisal-test", out var solution, FixedPoint2.New(10)));
                    Assert.That(solutions.TryAddReagent(solution!.Value, "Flavorol", FixedPoint2.New(10), out _));
                    Assert.That(pricing.GetPrice(sample, includeContents: false), Is.EqualTo(beforeReagents + 100));

                    var minds = em.System<MindSystem>();
                    var mind = minds.CreateMind(null);
                    minds.TransferTo(mind.Owner, uid);
                    Assert.That(mind.Comp.Objectives.Count, Is.EqualTo(1));
                    Assert.That(em.GetComponent<MetaDataComponent>(mind.Comp.Objectives[0]).EntityPrototype!.ID,
                        Is.EqualTo("SpecialSurviveObjective"));

                    Assert.That(em.HasComponent<NightVisionComponent>(uid));
                    Assert.That(em.HasComponent<ThermalVisionComponent>(uid));
                    Assert.That(em.HasComponent<PressureBreathingComponent>(uid));
                    Assert.That(em.GetComponent<HandsComponent>(uid).Hands, Is.Empty);
                    var boost = em.GetComponent<ShuttleBoostingPilotComponent>(uid);
                    Assert.That(boost.AngularMultiplier, Is.EqualTo(1.3f));
                    Assert.That(boost.AccelerationMultiplier, Is.EqualTo(1.3f));
                    var gas = em.System<AtmosphereSystem>().GetContainingMixture(uid);
                    Assert.That(gas, Is.Not.Null);
                    Assert.That(gas!.Pressure, Is.EqualTo(32).Within(.05));
                }

                Assert.That(count, Is.EqualTo(2));
            }
            finally
            {
                em.DeleteEntity(mapUid);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StanceDropsBothHandsCancelsWorkAndRespectsAmputation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid creature = default;
        EntityUid first = default;
        EntityUid second = default;
        DoAfterId? work = null;
        await server.WaitAssertion(() =>
        {
            creature = em.SpawnEntity("MobSpecial", new EntityCoordinates(map.Grid, .5f, .5f));
            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Upright));
            Assert.That(em.System<ActionBlockerSystem>().CanInteract(creature, null), Is.False);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var hands = em.GetComponent<HandsComponent>(creature);
            Assert.That(hands.Hands.Count, Is.EqualTo(2));
            Assert.That(em.System<ActionBlockerSystem>().CanInteract(creature, null));
            Assert.That(em.GetComponent<MeleeWeaponComponent>(creature).Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(20)));
            first = em.SpawnEntity("Crowbar", new EntityCoordinates(map.Grid, .5f, .5f));
            second = em.SpawnEntity("Wrench", new EntityCoordinates(map.Grid, .5f, .5f));
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(creature, first));
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(creature, second));
            var args = new DoAfterArgs(em, creature, TimeSpan.FromSeconds(30), new DumpableDoAfterEvent(), creature);
            Assert.That(em.System<SharedDoAfterSystem>().TryStartDoAfter(args, out work));
            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Quadruped));
            Assert.That(hands.Hands, Is.Empty);
            Assert.That(em.System<SharedDoAfterSystem>().GetStatus(work), Is.EqualTo(DoAfterStatus.Cancelled));
            Assert.That(em.GetComponent<TransformComponent>(first).ParentUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(em.GetComponent<TransformComponent>(second).ParentUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(creature, first), Is.False);
            Assert.That(em.GetComponent<MeleeWeaponComponent>(creature).Damage.DamageDict["Poison"], Is.EqualTo(FixedPoint2.New(5)));
            var body = em.System<SharedBodySystem>();
            var hand = body.GetBodyChildrenOfType(creature, BodyPartType.Hand).First();
            Assert.That(body.TryGetParentBodyPart(hand.Id, out var parent, out _));
            Assert.That(body.DetachPart(parent!.Value, hand.Component.ParentSlot!.Value, hand.Id));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Upright));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<HandsComponent>(creature).Hands.Count, Is.EqualTo(1)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnyGasWorksOnlyAboveMinimumPressureAndLanguageIsExclusive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var creature = em.Spawn("MobSpecial");
            try
            {
                var respiration = em.System<RespiratorSystem>();
                var lungs = em.GetComponent<PressureBreathingComponent>(creature);
                foreach (var gasId in Enum.GetValues<Gas>())
                {
                    var gas = new GasMixture(2500) { Temperature = 293.15f };
                    gas.SetMoles(gasId, 35);
                    Assert.That(respiration.CanMetabolizeGas((creature, null), gas), Is.True, gasId.ToString());
                    lungs.MinimumPressure = gas.Pressure;
                    Assert.That(respiration.CanMetabolizeGas((creature, null), gas), Is.False, "The pressure boundary is strict.");
                    lungs.MinimumPressure = 20;
                    gas.SetMoles(gasId, 10);
                    Assert.That(respiration.CanMetabolizeGas((creature, null), gas), Is.False);
                }

                var mixed = new GasMixture(2500) { Temperature = 293.15f };
                mixed.SetMoles(Gas.CarbonDioxide, 12);
                mixed.SetMoles(Gas.Nitrogen, 12);
                Assert.That(respiration.CanMetabolizeGas((creature, null), mixed), Is.True);
                var languages = em.System<LanguageSystem>();
                Assert.That(languages.CanSpeak((creature, null), "AntiTraeka"));
                Assert.That(languages.CanUnderstand((creature, null), "AntiTraeka"));
                Assert.That(languages.CanUnderstand((creature, null), "TauCetiBasic"), Is.False);
                Assert.That(languages.CanUnderstand((creature, null), "Universal"), Is.False);
                languages.AddLanguage(creature, "TauCetiBasic");
                Assert.That(languages.CanSpeak((creature, null), "TauCetiBasic"), Is.False);
            }
            finally
            {
                em.DeleteEntity(creature);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RegenerationCoversEveryDamageTypeButCannotReplaceBreathing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var creature = em.Spawn("MobSpecial");
            try
            {
                var passive = em.GetComponent<PassiveDamageComponent>(creature);
                Assert.That(passive.AllowedStates, Does.Contain(MobState.Alive).And.Contain(MobState.Critical));
                Assert.That(passive.AllowedStates, Does.Not.Contain(MobState.Dead));
                var damage = em.GetComponent<DamageableComponent>(creature);
                foreach (var type in damage.Damage.DamageDict.Keys)
                    Assert.That(passive.Damage.DamageDict.GetValueOrDefault(type), Is.LessThan(FixedPoint2.Zero), type);
                var lungs = em.GetComponent<PressureBreathingComponent>(creature);
                lungs.CanBreathe = true;
                var stance = em.GetComponent<LocomotionStanceComponent>(creature);
                stance.Stance = LocomotionStance.Curled;
                var pulse = new ModifyPassiveDamageEvent(passive.Damage);
                em.EventBus.RaiseLocalEvent(creature, ref pulse);
                Assert.That(pulse.Damage.DamageDict["Cellular"], Is.EqualTo(passive.Damage.DamageDict["Cellular"] * 2));
                lungs.CanBreathe = false;
                var airless = new ModifyPassiveDamageEvent(passive.Damage);
                em.EventBus.RaiseLocalEvent(creature, ref airless);
                Assert.That(airless.Damage.DamageDict.ContainsKey("Asphyxiation"), Is.False);
                Assert.That(passive.Damage.DamageDict.ContainsKey("Asphyxiation"), Is.True, "Do not mutate the template.");
            }
            finally
            {
                em.DeleteEntity(creature);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BreathesFromOwnTankThroughMaskEvenOnAllFours()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid map = default;
        EntityUid creature = default;
        EntityUid tank = default;
        await server.WaitAssertion(() =>
        {
            map = em.System<SharedMapSystem>().CreateMap();
            creature = em.SpawnEntity("MobSpecial", new EntityCoordinates(map, 0, 0));
            tank = em.SpawnEntity("JetpackMiniFilled", new EntityCoordinates(map, 0, 0));
            var inventory = em.System<InventorySystem>();
            Assert.That(inventory.TryEquip(creature, tank, "back", silent: true, force: true));
            Assert.That(em.GetComponent<LocomotionStanceComponent>(creature).Stance, Is.EqualTo(LocomotionStance.Quadruped));
            Assert.That(em.System<ActionBlockerSystem>().CanInteract(creature, null), Is.False);

            var action = em.GetComponent<ActionGrantComponent>(creature).ActionEntities
                .Single(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID == "ActionSpecialToggleInternals");
            Assert.That(em.GetComponent<InstantActionComponent>(action).CheckCanInteract, Is.False);
            var withoutMask = new ToggleInternalsActionEvent { Performer = creature };
            em.EventBus.RaiseLocalEvent(creature, withoutMask);
            Assert.That(withoutMask.Handled, Is.False, "The ordinary breath tool requirement still applies.");

            var mask = em.SpawnEntity("ClothingMaskBreath", new EntityCoordinates(map, 0, 0));
            Assert.That(inventory.TryEquip(creature, mask, "mask", silent: true, force: true));
            var toggle = new ToggleInternalsActionEvent { Performer = creature };
            em.EventBus.RaiseLocalEvent(creature, toggle);
            Assert.That(toggle.Handled);
            Assert.That(em.GetComponent<InternalsComponent>(creature).GasTankEntity, Is.EqualTo(tank));
        });
        await pair.RunSeconds(20);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<PressureBreathingComponent>(creature).CanBreathe, "Vacuum must not matter on internals.");
            Assert.That(em.GetComponent<DamageableComponent>(creature).Damage.DamageDict.GetValueOrDefault("Asphyxiation"),
                Is.EqualTo(FixedPoint2.Zero));
            em.DeleteEntity(map);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VacuumKillsAfterApproximatelyEightMinutesDespiteRestingRegeneration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid map = default;
        EntityUid creature = default;
        await server.WaitAssertion(() =>
        {
            map = em.System<SharedMapSystem>().CreateMap();
            creature = em.SpawnEntity("MobSpecial", new EntityCoordinates(map, 0, 0));
            var stance = em.GetComponent<LocomotionStanceComponent>(creature);
            Assert.That(em.System<LocomotionStanceSystem>().TryChangeStance((creature, stance), LocomotionStance.Curled));
        });
        await pair.RunSeconds(475);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MobStateComponent>(creature).CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(em.GetComponent<DamageableComponent>(creature).TotalDamage.Float(), Is.InRange(880, 899));
        });
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MobStateComponent>(creature).CurrentState, Is.EqualTo(MobState.Dead));
            em.DeleteEntity(map);
        });
        await pair.CleanReturnAsync();
    }
}
