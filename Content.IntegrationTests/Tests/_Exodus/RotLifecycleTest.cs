using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Actions;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Gravity;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class RotLifecycleTest
{
    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [Test]
    public async Task GhostConsumptionAllowsLarvaeAndCompletesBirth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid sated = default;
        EntityUid corpse = default;
        EntityUid larva = default;
        EntityUid brain = default;
        EntityUid mind = default;
        var organs = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            sated = em.SpawnEntity("MobRotSated", map.GridCoords);
            larva = em.SpawnEntity("MobRotLarva", map.GridCoords);
            corpse = em.SpawnEntity("MobHuman", map.GridCoords);
            organs.AddRange(em.System<BodySystem>().GetBodyOrgans(corpse).Select(organ => organ.Id));
            brain = organs.Single(uid => em.HasComponent<BrainComponent>(uid));
            var minds = em.System<SharedMindSystem>();
            mind = minds.CreateMind(null).Owner;
            minds.TransferTo(mind, corpse);
            em.RemoveComponent<HTNComponent>(sated);
            em.RemoveComponent<HTNComponent>(larva);
            em.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            server.PlayerMan.SetAttachedEntity(pair.Player!, sated);
            var parent = em.GetComponent<RotSatedComponent>(sated);
            parent.ConsumeDuration = TimeSpan.FromSeconds(4);
            parent.RiseDuration = parent.BirthDuration = TimeSpan.FromSeconds(0.2);
            Assert.That(parent.ConsumeActionEntity, Is.Not.Null);
            Assert.That(parent.StopActionEntity, Is.Not.Null);
            Assert.That(parent.StrikeActionEntity, Is.Not.Null);
            var action = new RotSatedConsumeActionEvent { Performer = sated, Target = corpse };
            em.EventBus.RaiseLocalEvent(sated, action);
            Assert.That(action.Handled, Is.True);
            Assert.That(em.GetComponent<RotCorpseClaimComponent>(corpse).Consumer, Is.EqualTo(sated));
            var child = em.GetComponent<RotLarvaComponent>(larva);
            child.BiteInterval = TimeSpan.FromSeconds(0.25);
            Assert.That(em.System<RotNestSystem>().TryFeed((larva, child), corpse), Is.True);
            Assert.That(child.FeedDoAfter, Is.Not.Null, "Feeding must use a visible DoAfter.");
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotLarvaComponent>(larva).Satiety, Is.EqualTo(1));
            Assert.That(em.EntityExists(corpse), Is.True);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(corpse), Is.False);
            foreach (var organ in organs)
            {
                Assert.That(em.EntityExists(organ), Is.True, "Consumption must preserve the victim's original organs.");
                Assert.That(em.GetComponent<OrganComponent>(organ).Body, Is.Null);
                Assert.That(em.System<SharedContainerSystem>().IsEntityInContainer(organ), Is.False);
                Assert.That(em.GetComponent<TransformComponent>(organ).MapUid, Is.EqualTo(map.MapUid));
            }
            Assert.That(em.GetComponent<MindContainerComponent>(brain).Mind, Is.EqualTo(mind));
            Assert.That(em.GetComponent<MindComponent>(mind).OwnedEntity, Is.EqualTo(brain));
            var parent = em.GetComponent<RotSatedComponent>(sated);
            Assert.That(parent.Activity, Is.EqualTo(RotSatedActivity.None));
            Assert.That(parent.PendingLarvae, Is.Zero);
            Assert.That(em.EntityQuery<RotLarvaComponent>().Count(), Is.EqualTo(5));

            var recipient = em.SpawnEntity("MobHuman", map.GridCoords);
            var bodies = em.System<BodySystem>();
            var recipientBrain = bodies.GetBodyOrgans(recipient).Single(organ => em.HasComponent<BrainComponent>(organ.Id));
            var head = em.GetComponent<TransformComponent>(recipientBrain.Id).ParentUid;
            Assert.That(bodies.RemoveOrgan(recipientBrain.Id), Is.True);
            Assert.That(bodies.InsertOrgan(head, brain, recipientBrain.Component.SlotId), Is.True);
            Assert.That(em.GetComponent<MindComponent>(mind).OwnedEntity, Is.EqualTo(recipient),
                "The preserved brain must allow the victim's mind to return to a body.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharedBloodPuddleIsFiniteAndFullLarvaPupatesAfterSearch()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid first = default;
        EntityUid second = default;
        EntityUid puddle = default;
        EntityUid full = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var solution = new Solution("Blood", FixedPoint2.New(2.5));
            solution.AddReagent("Water", FixedPoint2.New(10));
            Assert.That(em.System<PuddleSystem>().TrySpillAt(map.GridCoords, solution, out var spilled, sound: false), Is.True);
            puddle = spilled;
            first = em.SpawnEntity("MobRotLarva", map.GridCoords);
            second = em.SpawnEntity("MobRotLarva", map.GridCoords);
            foreach (var uid in new[] { first, second })
            {
                em.RemoveComponent<HTNComponent>(uid);
                var larva = em.GetComponent<RotLarvaComponent>(uid);
                larva.BiteInterval = TimeSpan.FromSeconds(0.25);
                larva.MaxSatiety = 1;
                Assert.That(em.System<RotNestSystem>().TryFeed((uid, larva), puddle), Is.True);
            }
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var a = em.GetComponent<RotLarvaComponent>(first);
            var b = em.GetComponent<RotLarvaComponent>(second);
            Assert.That(a.Satiety + b.Satiety, Is.EqualTo(1), "A simultaneous bite must not duplicate the last blood portion.");
            full = a.Satiety == 1 ? first : second;
            var larva = em.GetComponent<RotLarvaComponent>(full);
            Assert.That(larva.HatchAt, Is.Null);
            Assert.That(larva.PupateBy, Is.Not.Null);
            var left = larva.PupateBy!.Value - server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(left.TotalSeconds, Is.InRange(22, 48));
            Assert.That(em.System<SharedAppearanceSystem>().TryGetData<RotLarvaState>(full, RotLarvaVisuals.State, out var state), Is.True);
            Assert.That(state, Is.EqualTo(RotLarvaState.Sated));
            var puddleComponent = em.GetComponent<PuddleComponent>(puddle);
            Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(puddle, puddleComponent.SolutionName, out _, out var remaining), Is.True);
            Assert.That(remaining.GetReagentQuantity(new("Blood", null)), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(remaining.GetReagentQuantity(new("Water", null)), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(em.System<RotNestSystem>().CanFeedOn((first, a), puddle), Is.False);
        });
        await pair.RunSeconds(49);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotLarvaComponent>(full).HatchAt, Is.Not.Null);
            Assert.That(em.System<SharedAppearanceSystem>().TryGetData<RotLarvaState>(full, RotLarvaVisuals.State, out var state), Is.True);
            Assert.That(state, Is.EqualTo(RotLarvaState.Pupa));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SatedRemembersAggressorsAndRejectsGlassCover()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid sated = default;
        EntityUid attacker = default;
        EntityUid wall = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -8; x <= 8; x++)
            {
                for (var y = -8; y <= 8; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            sated = em.SpawnEntity("MobRotSated", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            attacker = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            em.RemoveComponent<HTNComponent>(sated);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", FixedPoint2.New(1));
            em.System<DamageableSystem>().TryChangeDamage(sated, damage, origin: attacker);
            var comp = em.GetComponent<RotSatedComponent>(sated);
            var defender = em.GetComponent<RotDefenderComponent>(sated);
            Assert.That(defender.Enemies, Does.Contain(attacker));
            em.System<RotSatedSystem>().Stop((sated, comp));
            Assert.That(defender.Enemies, Does.Contain(attacker), "HTN shutdown must not erase character memory.");
            comp.NextThink = TimeSpan.Zero;
            em.System<RotSatedSystem>().Think((sated, comp));
            Assert.That(defender.Target, Is.EqualTo(attacker), "A remembered aggressor is attacked without a new provocation.");
            em.System<RotSatedSystem>().Stop((sated, comp));
            defender.Threat = attacker;
            wall = em.SpawnEntity("Window", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var defender = em.GetComponent<RotDefenderComponent>(sated);
            Assert.That(em.System<RotDefenderSystem>().IsCovered((sated, defender), em.GetComponent<TransformComponent>(sated).Coordinates), Is.False);
            em.DeleteEntity(wall);
            wall = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var defender = em.GetComponent<RotDefenderComponent>(sated);
            Assert.That(em.System<RotDefenderSystem>().IsCovered((sated, defender), em.GetComponent<TransformComponent>(sated).Coordinates), Is.True);
            em.System<MobStateSystem>().ChangeMobState(sated, MobState.Critical);
            Assert.That(defender.Enemies, Is.Not.Empty, "Incapacitation must not erase living aggressor memory.");
            em.System<MobStateSystem>().ChangeMobState(sated, MobState.Dead);
            Assert.That(defender.Enemies, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpaceTargetsDoNotBlockRetargetingAndDamageInterruptsBothRests()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -10; x <= 10; x++)
            {
                for (var y = -10; y <= 10; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var sated = em.SpawnEntity("MobRotSated", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            em.RemoveComponent<HTNComponent>(sated);
            var shooter = em.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, 15f, 0.5f));
            var reachable = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -2f, 0.5f));
            em.SpawnEntity("RotGrowth", new EntityCoordinates(map.Grid, 4.5f, 4.5f));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", FixedPoint2.New(1));
            var damageSystem = em.System<DamageableSystem>();
            damageSystem.TryChangeDamage(sated, damage, origin: shooter);
            damageSystem.TryChangeDamage(sated, damage, origin: reachable);
            var comp = em.GetComponent<RotSatedComponent>(sated);
            var defender = em.GetComponent<RotDefenderComponent>(sated);
            var system = em.System<RotSatedSystem>();
            comp.NextThink = TimeSpan.Zero;
            system.Think((sated, comp));
            Assert.That(defender.Target, Is.EqualTo(reachable));
            em.System<SharedTransformSystem>().SetCoordinates(reachable, new EntityCoordinates(map.MapUid, 15f, 2f));
            comp.NextThink = TimeSpan.Zero;
            system.Think((sated, comp));
            Assert.That(defender.Target, Is.Null);
            Assert.That(defender.Route, Is.EqualTo(RotDefenderRoute.Cover));

            system.Stop((sated, comp));
            defender.Threat = shooter;
            defender.Route = RotDefenderRoute.Cover;
            defender.RestUntil = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(14);
            damageSystem.TryChangeDamage(sated, damage);
            comp.NextThink = TimeSpan.Zero;
            system.Think((sated, comp));
            Assert.That(defender.Route, Is.EqualTo(RotDefenderRoute.Colony), "Environmental damage interrupts cover and triggers retreat to rot.");
            Assert.That(defender.EscapePath, Is.Not.Null);

            system.Stop((sated, comp));
            defender.Route = RotDefenderRoute.Colony;
            defender.RestUntil = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(12);
            damageSystem.TryChangeDamage(sated, damage, origin: shooter);
            comp.NextThink = TimeSpan.Zero;
            system.Think((sated, comp));
            Assert.That(defender.Route, Is.EqualTo(RotDefenderRoute.Cover), "Fire at the colony must interrupt its rest too.");
            system.Stop((sated, comp));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FullLarvaWalksToFreeThreeWallShelter()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid larva = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var gravity = em.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = gravity.Enabled = true;
            foreach (var x in new[] { -3, 3 })
            {
                em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + 0.5f, 1.5f));
                em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + 0.5f, -0.5f));
                em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + Math.Sign(x) + 0.5f, 0.5f));
            }
            var occupied = em.SpawnEntity("MobRotLarva", new EntityCoordinates(map.Grid, -2.5f, 0.5f));
            em.RemoveComponent<HTNComponent>(occupied);
            em.GetComponent<RotLarvaComponent>(occupied).HatchAt = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(2);
        });
        // Navmesh updates are deferred. Wait until the new floor replaces space in the preferred alcove.
        await PoolManager.WaitUntil(server, () =>
            em.System<PathfindingSystem>().GetPoly(new EntityCoordinates(map.Grid, 3.5f, 0.5f))?.Data.IsFreeSpace == true);
        await server.WaitAssertion(() =>
        {
            larva = em.SpawnEntity("MobRotLarva", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var comp = em.GetComponent<RotLarvaComponent>(larva);
            comp.Satiety = comp.MaxSatiety;
            comp.PupateBy = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(48);
        });
        await pair.RunSeconds(18);
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<RotLarvaComponent>(larva);
            Assert.That(comp.HatchAt, Is.Not.Null, "A full larva should travel to the free alcove before its search expires.");
            var tile = em.System<SharedMapSystem>().TileIndicesFor(map.Grid, map.Grid.Comp, em.GetComponent<TransformComponent>(larva).Coordinates);
            Assert.That(tile, Is.EqualTo(new Vector2i(3, 0)));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GhostGroundStrikeDamagesOnlyTheThreeByThreeArea(bool hungry)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid creature = default;
        EntityUid adjacent = default;
        EntityUid outside = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -3; x <= 3; x++)
            {
                for (var y = -3; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            creature = em.SpawnEntity(hungry ? "MobRotHungry" : "MobRotSated", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            em.RemoveComponent<HTNComponent>(creature);
            var actionEntity = hungry
                ? em.GetComponent<RotHungryComponent>(creature).StrikeActionEntity
                : em.GetComponent<RotSatedComponent>(creature).StrikeActionEntity;
            Assert.That(actionEntity, Is.Not.Null);
            adjacent = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            outside = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            InstantActionEvent action = hungry
                ? new RotHungryStrikeActionEvent { Performer = creature }
                : new RotSatedStrikeActionEvent { Performer = creature };
            em.EventBus.RaiseLocalEvent(creature, (object) action);
            Assert.That(action.Handled, Is.True);
            Assert.That(em.GetComponent<DamageableComponent>(adjacent).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(em.GetComponent<DamageableComponent>(outside).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            InstantActionEvent repeated = hungry
                ? new RotHungryStrikeActionEvent { Performer = creature }
                : new RotSatedStrikeActionEvent { Performer = creature };
            em.EventBus.RaiseLocalEvent(creature, (object) repeated);
            Assert.That(repeated.Handled, Is.False, "The manual action must obey the shared attack cooldown.");
        });
        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task TrappedSatedBreaksOutWhenEscapePathsFail()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var walls = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -8; x <= 8; x++)
            {
                for (var y = -8; y <= 8; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var gravity = em.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = gravity.Enabled = true;
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x != 0 || y != 0)
                        walls.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f)));
                }
            }
            var sated = em.SpawnEntity("MobRotSated", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var shooter = em.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, 12.5f, 0.5f));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", FixedPoint2.New(1));
            em.System<DamageableSystem>().TryChangeDamage(sated, damage, origin: shooter);
        });
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(walls.Any(uid => !em.EntityExists(uid)
                || em.GetComponent<DamageableComponent>(uid).TotalDamage > FixedPoint2.Zero), Is.True,
                "Failed cover paths must eventually trigger a ground strike, rather than resetting the stuck timer forever.");
        });
        await pair.CleanReturnAsync();
    }

}
