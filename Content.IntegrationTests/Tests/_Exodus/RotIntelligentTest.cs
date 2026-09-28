using Content.Shared._Exodus.Shuttles; // Exodus
using System;
using System.IO;
using System.Linq;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server._NF.Shuttles.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
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

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed partial class RotIntelligentTest
{
    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [Test]
    public async Task ConstructionReservesFootprintAndSeveringStopsIncome()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid producer = default;
        EntityUid bridge = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -5; x <= 15; x++)
                for (var y = -5; y <= 8; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotIntelligentSystem>();
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.NetworkReady, Is.True);
            Assert.That(brain.BuildAction, Is.Not.Null);
            Assert.That(system.TryQueueBuilding((core, brain), new EntityCoordinates(map.Grid, 2.5f, .5f), "RotBuildTissue", 0), Is.True);
            Assert.That(system.TryQueueBuilding((core, brain), new EntityCoordinates(map.Grid, 2.5f, .5f), "RotBuildTissue", 0), Is.False);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Projects.Count, Is.EqualTo(1));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.Projects, Is.Empty);
            Assert.That(state.Reservations, Is.Empty);
            Assert.That(state.Connected.Contains(new Vector2i(2, 0)), Is.True);
            var system = em.System<RotIntelligentSystem>();
            for (var x = 3; x <= 6; x++)
            {
                var tissue = em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, x + .5f, .5f));
                system.Join(tissue, core);
                if (x == 3)
                    bridge = tissue;
            }
            for (var x = 4; x <= 6; x++)
                for (var y = 1; y <= 2; y++)
                    system.Join(em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, x + .5f, y + .5f)), core);
            producer = em.SpawnEntity("RotProducer", new EntityCoordinates(map.Grid, 4.5f, .5f));
            system.Join(producer, core);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotColonyMemberComponent>(producer).Connected, Is.True);
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Income, Is.EqualTo(2));
            em.DeleteEntity(bridge);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(producer).Connected, Is.False,
                "A severed organ must stop immediately, before the incremental graph rebuild.");
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotColonyMemberComponent>(producer).Connected, Is.False);
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Income, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostEyePrivateVisionAndPermanentGridRelease()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid other = default;
        EntityUid eye = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -3; x <= 35; x++)
                for (var y = -3; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            other = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 30.5f, .5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
            eye = em.GetComponent<RotIntelligentComponent>(core).Eye!.Value;
            Assert.That(em.EntityExists(eye), Is.True);
            Assert.That(em.HasComponent<RelayInputMoverComponent>(core), Is.True);
            em.EnsureComponent<ForceAnchorComponent>(map.Grid).Bedrock = true;
            em.EnsureComponent<ForceAnchorPostFTLComponent>(map.Grid);
            em.EnsureComponent<PreventGridAnchorChangesComponent>(map.Grid);
            var shuttle = em.EnsureComponent<ShuttleComponent>(map.Grid);
            Assert.That(em.System<ShuttleSystem>().TrySetEnabled((map.Grid, shuttle), false, force: true), Is.True);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotIntelligentSystem>();
            Assert.That(system.CanSee(core, new EntityCoordinates(map.Grid, 2.5f, .5f)), Is.True);
            Assert.That(system.CanSee(core, new EntityCoordinates(map.Grid, 30.5f, .5f)), Is.False,
                "Another colony's core cannot grant vision.");
            Assert.That(system.Join(other, core), Is.False);
            Assert.That(system.TryUnanchor((core, em.GetComponent<RotIntelligentComponent>(core))), Is.False,
                "A bedrock anchor must be immune to the core's release action.");
            Assert.That(em.HasComponent<PreventGridAnchorChangesComponent>(map.Grid), Is.True);
            Assert.That(em.HasComponent<GridAnchorReleasedComponent>(map.Grid), Is.False);
            em.GetComponent<ForceAnchorComponent>(map.Grid).Bedrock = false;
            Assert.That(system.TryUnanchor((core, em.GetComponent<RotIntelligentComponent>(core))), Is.True);
            Assert.That(em.GetComponent<ShuttleComponent>(map.Grid).Enabled, Is.True);
            Assert.That(em.HasComponent<ForceAnchorComponent>(map.Grid), Is.True, "Keep the station identity used by jobs.");
            Assert.That(em.GetComponent<PreventGridAnchorChangesComponent>(map.Grid).Running, Is.False);
            Assert.That(em.HasComponent<GridAnchorReleasedComponent>(map.Grid), Is.True);
            em.System<MobStateSystem>().ChangeMobState(core, MobState.Dead);
        });
        await pair.RunSeconds(.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(eye), Is.False);
            Assert.That(em.HasComponent<PreventGridAnchorChangesComponent>(map.Grid), Is.False);
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Alive, Is.False);
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Income, Is.Zero);
            Assert.That(em.GetComponent<ShuttleComponent>(map.Grid).Enabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NesterProducesOneFiniteMealForTwoLarvae()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid nester = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid third = default;
        EntityUid food = default;
        await server.WaitAssertion(() =>
        {
            nester = em.SpawnEntity("MobRotNester", map.GridCoords);
            first = em.SpawnEntity("MobRotLarva", map.GridCoords);
            second = em.SpawnEntity("MobRotLarva", map.GridCoords);
            third = em.SpawnEntity("MobRotLarva", map.GridCoords);
            foreach (var uid in new[] { first, second, third })
            {
                em.RemoveComponent<HTNComponent>(uid);
                em.GetComponent<RotLarvaComponent>(uid).BiteInterval = TimeSpan.FromSeconds(.15);
            }
            var parent = em.GetComponent<RotNesterComponent>(nester);
            parent.Reserve = parent.FillDuration;
            parent.VomitDuration = TimeSpan.FromSeconds(.25);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var meals = em.EntityQueryEnumerator<RotNutritionBlobComponent>();
            var count = 0;
            while (meals.MoveNext(out var uid, out var blob))
            {
                food = uid;
                count++;
                Assert.That(blob.Remaining, Is.EqualTo(2 * em.GetComponent<RotLarvaComponent>(first).MaxSatiety));
            }
            Assert.That(count, Is.EqualTo(1));
            Assert.That(em.GetComponent<RotNesterComponent>(nester).Reserve.TotalSeconds, Is.LessThan(3));
        });
        for (var bite = 0; bite < 4; bite++)
        {
            await server.WaitAssertion(() =>
            {
                var nest = em.System<RotNestSystem>();
                Assert.That(nest.TryFeed((first, em.GetComponent<RotLarvaComponent>(first)), food), Is.True);
                Assert.That(nest.TryFeed((second, em.GetComponent<RotLarvaComponent>(second)), food), Is.True);
            });
            await pair.RunSeconds(.5f);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(food), Is.False);
            Assert.That(em.GetComponent<RotLarvaComponent>(first).Satiety, Is.EqualTo(4));
            Assert.That(em.GetComponent<RotLarvaComponent>(second).Satiety, Is.EqualTo(4));
            Assert.That(em.GetComponent<RotLarvaComponent>(third).Satiety, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }
}
