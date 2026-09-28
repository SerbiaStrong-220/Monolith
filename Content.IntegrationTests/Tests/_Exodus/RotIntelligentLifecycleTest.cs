using System;
using System.Collections.Generic;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task NurseryKeepsItsOffspringAcrossDisconnectionPauseAndBlockedExit()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid nursery = default;
        EntityUid bridge = default;
        var walls = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var colony = em.System<RotIntelligentSystem>();
            for (var x = -2; x <= 8; x++)
                for (var y = -2; y <= 4; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            bridge = em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, 2.5f, .5f));
            colony.Join(bridge, core);
            for (var x = 3; x <= 4; x++)
                for (var y = 0; y <= 1; y++)
                    colony.Join(em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, x + .5f, y + .5f)), core);
            nursery = em.SpawnEntity("RotNursery", new EntityCoordinates(map.Grid, 3.5f, .5f));
            colony.Join(nursery, core);
            var brood = em.GetComponent<RotNurseryComponent>(nursery);
            Assert.That(brood.Selected, Is.Not.Null);
            brood.Selected = "MobRotNester";
            brood.RemainingOffspring.Clear();
            brood.Duration = brood.Remaining = TimeSpan.FromSeconds(3);
            for (var x = 2; x <= 5; x++)
                for (var y = -1; y <= 2; y++)
                    walls.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, x + .5f, y + .5f)));
        });
        await pair.RunSeconds(.6f);
        TimeSpan remaining = default;
        await server.WaitAssertion(() =>
        {
            em.DeleteEntity(bridge);
            remaining = em.GetComponent<RotNurseryComponent>(nursery).Remaining;
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotNurseryComponent>(nursery).Remaining, Is.EqualTo(remaining));
            bridge = em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, 2.5f, .5f));
            em.System<RotIntelligentSystem>().Join(bridge, core);
        });
        await pair.RunSeconds(.2f);
        await server.WaitAssertion(() =>
        {
            em.System<SharedMapSystem>().SetPaused(em.GetComponent<TransformComponent>(core).MapID, true);
            remaining = em.GetComponent<RotNurseryComponent>(nursery).Remaining;
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotNurseryComponent>(nursery).Remaining, Is.EqualTo(remaining));
            em.System<SharedMapSystem>().SetPaused(em.GetComponent<TransformComponent>(core).MapID, false);
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            var brood = em.GetComponent<RotNurseryComponent>(nursery);
            Assert.That(brood.Remaining, Is.EqualTo(TimeSpan.Zero));
            Assert.That(brood.Finished, Is.False);
            Assert.That(brood.Selected!.Value.Id, Is.EqualTo("MobRotNester"));
            foreach (var wall in walls)
                em.DeleteEntity(wall);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(nursery), Is.False);
            var children = em.EntityQueryEnumerator<RotNesterComponent, RotColonyMemberComponent>();
            var count = 0;
            while (children.MoveNext(out _, out _, out var member))
            {
                Assert.That(member.Core, Is.EqualTo(core));
                count++;
            }
            Assert.That(count, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrivateVisionRespectsWallsGlassAndSourceDeath()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        var barriers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -10; x <= 32; x++)
                for (var y = -9; y <= 9; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            for (var y = -9; y <= 9; y++)
                barriers.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, y + .5f)));
        });
        await pair.RunSeconds(.2f);
        await server.WaitAssertion(() =>
        {
            var colony = em.System<RotIntelligentSystem>();
            var target = new EntityCoordinates(map.Grid, 4.5f, .5f);
            Assert.That(colony.CanSee(core, target), Is.False);
            foreach (var wall in barriers)
                em.DeleteEntity(wall);
            for (var y = -9; y <= 9; y++)
                em.SpawnEntity("Window", new EntityCoordinates(map.Grid, 2.5f, y + .5f));
        });
        await pair.RunSeconds(.2f);
        EntityUid mobile = default;
        await server.WaitAssertion(() =>
        {
            var colony = em.System<RotIntelligentSystem>();
            var target = new EntityCoordinates(map.Grid, 4.5f, .5f);
            Assert.That(colony.CanSee(core, target), Is.True);
            mobile = em.SpawnEntity("MobRotHungry", new EntityCoordinates(map.Grid, 22.5f, .5f));
            Assert.That(colony.Join(mobile, core), Is.True);
        });
        await pair.RunSeconds(.2f);
        await server.WaitAssertion(() =>
        {
            var colony = em.System<RotIntelligentSystem>();
            var remote = new EntityCoordinates(map.Grid, 25.5f, .5f);
            Assert.That(colony.CanSee(core, remote), Is.True);
            em.System<MobStateSystem>().ChangeMobState(mobile, MobState.Dead);
            Assert.That(colony.CanSee(core, remote), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PilotingRestoresEyeAndUnanchorSurvivesFtl()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
            var colony = em.System<RotIntelligentSystem>();
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            em.EnsureComponent<Content.Server._NF.Shuttles.Components.ForceAnchorPostFTLComponent>(map.Grid);
            Assert.That(colony.TryUnanchor((core, brain)), Is.True);
            Assert.That(colony.TryPilot((core, brain)), Is.True);
            Assert.That(em.HasComponent<PilotComponent>(core), Is.True);
            Assert.That(em.HasComponent<RelayInputMoverComponent>(core), Is.False);
            em.System<ShuttleConsoleSystem>().RemovePilot(core);
            Assert.That(em.GetComponent<RelayInputMoverComponent>(core).RelayEntity, Is.EqualTo(brain.Eye));
            var completed = new FTLCompletedEvent(map.Grid, em.GetComponent<TransformComponent>(core).MapUid!.Value);
            em.EventBus.RaiseLocalEvent(map.Grid, ref completed);
            Assert.That(em.GetComponent<ShuttleComponent>(map.Grid).Enabled, Is.True);
        });
        await pair.RunSeconds(.2f);
        await server.WaitAssertion(() => Assert.That(em.HasComponent<RotPilotLockComponent>(map.Grid), Is.False));
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task OrganicThrustersRegisterOnceAndStopOnSevering(int rotation)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid engine = default;
        EntityUid bridge = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var colony = em.System<RotIntelligentSystem>();
            for (var x = -5; x <= 5; x++)
                for (var y = -5; y <= 5; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var origin = RotGeometry.Rotate(new Vector2i(2, 0), rotation);
            em.RemoveComponent<ShuttleComponent>(map.Grid);
            foreach (var tile in RotGeometry.Cells(origin, new Vector2i(1, 3), rotation))
            {
                var tissue = em.SpawnEntity("RotTissue", maps.GridTileToLocal(map.Grid, map.Grid.Comp, tile));
                colony.Join(tissue, core);
                if (tile == origin)
                    bridge = tissue;
            }
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, origin + RotGeometry.Rotate(new Vector2i(0, 3), rotation), Tile.Empty);
            engine = em.SpawnEntity("RotFlagellum", maps.GridTileToLocal(map.Grid, map.Grid.Comp, origin));
            em.System<SharedTransformSystem>().SetLocalRotation(engine, Angle.FromDegrees(rotation * 90));
            colony.Join(engine, core);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ThrusterComponent>(engine).IsOn, Is.False);
            Assert.That(em.System<RotIntelligentSystem>().TryUnanchor((core, em.GetComponent<RotIntelligentComponent>(core))), Is.True);
        });
        await pair.RunSeconds(1.1f);
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var engineComp = em.GetComponent<ThrusterComponent>(engine);
            Assert.That(engineComp.IsOn, Is.True);
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            var count = 0;
            foreach (var direction in shuttle.LinearThrusters)
                foreach (var uid in direction)
                {
                    if (uid == engine)
                        count++;
                }
            Assert.That(count, Is.EqualTo(1));
            em.DeleteEntity(bridge);
            Assert.That(engineComp.IsOn, Is.False);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            foreach (var direction in shuttle.LinearThrusters)
                Assert.That(direction, Does.Not.Contain(engine));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InterruptedConstructionReturnsOnlyUnspentBiomass()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid marker = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 4; x++)
                for (var y = -2; y <= 4; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.BaseIncome = brain.Income = 0;
            var before = brain.Biomass;
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, brain),
                new EntityCoordinates(map.Grid, 1.5f, .5f), "RotBuildEyeball", 0), Is.True);
            foreach (var uid in em.GetComponent<RotColonyStateComponent>(core).Projects)
                marker = uid;
            var job = em.GetComponent<RotConstructionComponent>(marker);
            job.Started = server.ResolveDependency<IGameTiming>().CurTime - job.Duration / 2;
            em.DeleteEntity(marker);
            Assert.That(brain.Biomass, Is.EqualTo(before - job.Reserved / 2).Within(.01));
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Projects, Is.Empty);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Reservations, Is.Empty);
        });
        await pair.RunSeconds(7);
        await server.WaitAssertion(() =>
        {
            var organs = em.EntityQueryEnumerator<RotColonyMemberComponent>();
            while (organs.MoveNext(out var uid, out var member))
                Assert.That(member.Core != core || member.VisionRange < 24, Is.True, $"Cancelled organ spawned: {uid}");
        });
        await pair.CleanReturnAsync();
    }
}
