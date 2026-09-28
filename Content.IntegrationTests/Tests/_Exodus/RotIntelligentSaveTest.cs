using Content.Shared._Exodus.Shuttles; // Exodus
using System;
using System.IO;
using Content.Server.Body.Systems;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.StationAi;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task UninitializedColonyDefersBloodAndVisionUntilMapInit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap(false, "FloorSteel");
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var eyeball = em.SpawnEntity("RotEyeball", new EntityCoordinates(map.Grid, 1.5f, .5f));
            Assert.That(em.GetComponent<SolutionContainerManagerComponent>(core).Solutions, Is.Null);
            Assert.That(em.HasComponent<StationAiVisionComponent>(core), Is.False);
            Assert.That(em.HasComponent<StationAiVisionComponent>(eyeball), Is.False);

            maps.InitializeMap(map.MapId);

            Assert.That(em.System<BloodstreamSystem>().GetBloodLevelPercentage(core), Is.EqualTo(1f));
            var coreVision = em.GetComponent<StationAiVisionComponent>(core);
            Assert.That(coreVision.Network, Is.EqualTo(core));
            Assert.That(coreVision.Enabled, Is.True);
            var detachedVision = em.GetComponent<StationAiVisionComponent>(eyeball);
            Assert.That(detachedVision.Network, Is.EqualTo(eyeball));
            Assert.That(detachedVision.Enabled, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavedColonyKeepsOwnershipBiomassNurseryResultAndReleasedGrid()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid nursery = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 3; x++)
                for (var y = -2; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            nursery = em.SpawnEntity("RotNursery", new EntityCoordinates(map.Grid, -.5f, -.5f));
            em.System<RotIntelligentSystem>().Join(nursery, core);
            em.EnsureComponent<ForceAnchorComponent>(map.Grid);
            em.EnsureComponent<ForceAnchorPostFTLComponent>(map.Grid);
            em.System<RotIntelligentSystem>().TryUnanchor((core, em.GetComponent<RotIntelligentComponent>(core)));
        });
        await pair.RunSeconds(.5f);
        TimeSpan remaining = default;
        EntityUid loadedGrid = default;
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.BaseIncome = brain.Income = 0;
            brain.Biomass = 77;
            var brood = em.GetComponent<RotNurseryComponent>(nursery);
            brood.Selected = "MobRotSated";
            brood.RemainingOffspring.Clear();
            brood.RemainingOffspring.Add("MobRotSpawn");
            brood.RemainingOffspring.Add("MobRotSpawn");
            brood.Remaining = remaining = TimeSpan.FromSeconds(73);
            var loader = em.System<MapLoaderSystem>();
            using var text = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, text), Is.True);
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(text.ToString());
            Assert.That(loader.TryLoadGrid(reader, "rot-colony-save-test", out _, out var grid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            loadedGrid = grid!.Value.Owner;
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var cores = em.EntityQueryEnumerator<RotIntelligentComponent>();
            Assert.That(cores.MoveNext(out var loadedCore, out var brain), Is.True);
            Assert.That(loadedCore, Is.Not.EqualTo(core));
            Assert.That(cores.MoveNext(out _, out _), Is.False);
            Assert.That(brain.Biomass, Is.EqualTo(77));
            Assert.That(brain.NetworkReady, Is.True);
            var nurseries = em.EntityQueryEnumerator<RotNurseryComponent, RotColonyMemberComponent>();
            Assert.That(nurseries.MoveNext(out _, out var brood, out var member), Is.True);
            Assert.That(member.Core, Is.EqualTo(loadedCore));
            Assert.That(member.Connected, Is.True);
            Assert.That(brood.Selected!.Value.Id, Is.EqualTo("MobRotSated"));
            Assert.That(brood.RemainingOffspring.Count, Is.EqualTo(2));
            Assert.That(brood.RemainingOffspring[0].Id, Is.EqualTo("MobRotSpawn"));
            Assert.That(brood.Remaining.TotalSeconds, Is.InRange(remaining.TotalSeconds - 1, remaining.TotalSeconds));
            Assert.That(em.HasComponent<GridAnchorReleasedComponent>(loadedGrid), Is.True);
            Assert.That(em.HasComponent<ForceAnchorComponent>(loadedGrid), Is.True);
            Assert.That(em.GetComponent<ShuttleComponent>(loadedGrid).Enabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
