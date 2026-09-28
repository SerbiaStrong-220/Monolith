using System.Collections.Generic;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task AdditiveRebuildKeepsOrgansRunningAndUnrelatedTilesDoNotInvalidate()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid organ = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 10; x++)
                for (var y = -2; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            organ = em.SpawnEntity("RotProducer", new EntityCoordinates(map.Grid, -.5f, -.5f));
            em.System<RotIntelligentSystem>().Join(organ, core);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotIntelligentSystem>();
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.NetworkReady, Is.True);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.True);
            var income = brain.Income;
            system.Join(em.SpawnEntity("MobRotLarva", new EntityCoordinates(map.Grid, .5f, .5f)), core);
            Assert.That(brain.NetworkReady, Is.True, "A mobile member does not change the tissue graph.");
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(10, 0), Tile.Empty);
            Assert.That(brain.NetworkReady, Is.True, "An unrelated floor tile must not invalidate the colony.");
            brain.NetworkBudget = 1;
            system.Join(em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, 2.5f, .5f)), core);
            Assert.That(brain.NetworkReady, Is.False);
            Assert.That(brain.ConstructionAvailable, Is.True, // Exodus construction-queue
                "An additive graph rebuild must not block construction on the already valid network.");
            Assert.That(system.TryQueueBuilding((core, brain),
                new EntityCoordinates(map.Grid, 2.5f, 1.5f), "RotBuildTissue", 0), Is.True);
            Assert.That(brain.Income, Is.EqualTo(income));
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.True);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).NetworkReady, Is.False);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.True);
            // Cutting during the additive rebuild must still turn organs off immediately.
            em.System<RotIntelligentSystem>().MarkNetworkDirty(core);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.False);
            for (var i = 0; i < 10; i++)
                em.System<RotIntelligentSystem>().MarkNetworkDirty(core);
        });
        var reconnected = false;
        for (var i = 0; i < 128 && !reconnected; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() => reconnected = em.GetComponent<RotColonyMemberComponent>(organ).Connected);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(reconnected, Is.True, "Repeated invalidation must still allow the next rebuild to finish.");
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).NetworkReady, Is.False,
                "The organ reconnects during the budgeted apply phase, before the whole network is ready.");
            em.System<RotIntelligentSystem>().MarkNetworkDirty(core);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.False,
                "A second cut must also disable organs reconnected by a partial rebuild.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharedNetworkBudgetLimitsAllColoniesAndDoesNotStarveLaterCores()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var cores = new List<EntityUid>();
        var previousBudget = server.CfgMan.GetCVar(EXCVars.RotNetworkBudget);
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(EXCVars.RotNetworkBudget, 8);
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 30; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var i = 0; i < 4; i++)
                cores.Add(em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, i * 8 + .5f, .5f)));
        });
        for (var i = 0; i < 30; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() => Assert.That(em.System<RotIntelligentSystem>().LastNetworkWork, Is.InRange(0, 8)));
        }
        await server.WaitAssertion(() =>
        {
            foreach (var core in cores)
                Assert.That(em.GetComponent<RotIntelligentComponent>(core).NetworkReady, Is.True);
            server.CfgMan.SetCVar(EXCVars.RotNetworkBudget, previousBudget);
        });
        await pair.CleanReturnAsync();
    }
}
