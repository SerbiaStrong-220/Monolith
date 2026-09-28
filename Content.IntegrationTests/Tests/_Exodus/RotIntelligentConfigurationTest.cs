using System;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task OrganGrowthCompletesAndSubsequentAppearanceStillUpdates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid organ = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            organ = em.SpawnEntity("RotEyeball", new EntityCoordinates(map.Grid, 1.5f, .5f));
            Assert.That(em.System<RotIntelligentSystem>().Join(organ, core), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
        });
        await pair.RunSeconds(2);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.EntMan;
            var uid = pair.ToClientUid(organ);
            var sprites = client.System<SpriteSystem>();
            Assert.That(sprites.LayerGetRsiState(uid, 0).ToString(), Is.EqualTo("alive"));
        });
        await server.WaitAssertion(() => em.System<SharedTransformSystem>().Unanchor(organ));
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.False));
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.EntMan;
            Assert.That(client.System<SpriteSystem>().LayerGetRsiState(pair.ToClientUid(organ), 0).ToString(), Is.EqualTo("dormant"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConstructionRecipesMatchTheEntitiesTheyBuild()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var factory = pair.Server.ResolveDependency<IComponentFactory>();
            foreach (var recipe in prototypes.EnumeratePrototypes<RotBuildingPrototype>())
            {
                var entity = prototypes.Index(recipe.Entity);
                Assert.That(entity.TryGetComponent<RotColonyMemberComponent>(out var member, factory), Is.True, recipe.ID);
                Assert.Multiple(() =>
                {
                    Assert.That(member!.Size, Is.EqualTo(recipe.Size), recipe.ID + " reserves its actual footprint.");
                    Assert.That(member.RequiresExhaust, Is.EqualTo(recipe.RequiresExhaust), recipe.ID);
                    Assert.That(recipe.Duration.TotalSeconds, Is.GreaterThan(0), recipe.ID);
                    Assert.That(recipe.Cost, Is.GreaterThanOrEqualTo(0), recipe.ID);
                });
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SmallNetworkBudgetAcceptsConstructionWithoutAnotherClick()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 45; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.NetworkBudget = 4;
            brain.BaseIncome = 0;
            // Isolate manual construction from the periodic automatic growth tick.
            em.GetComponent<RotSpreadComponent>(core).NextGrowth += TimeSpan.FromMinutes(1);
            var colony = em.System<RotIntelligentSystem>();
            for (var x = 2; x <= 40; x++)
                colony.Join(em.SpawnEntity("RotTissue", new EntityCoordinates(map.Grid, x + .5f, .5f)), core);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.NetworkReady, Is.False);
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, brain),
                new EntityCoordinates(map.Grid, 2.5f, 1.5f), "RotBuildTissue", 0), Is.True,
                em.GetComponent<RotColonyStateComponent>(core).Feedback);
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.Projects.Count, Is.EqualTo(1));
            var job = state.Reservations[new Vector2i(2, 1)];
            Assert.That(em.GetComponent<RotConstructionComponent>(job).WaitingForNetwork, Is.True);
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, brain),
                new EntityCoordinates(map.Grid, 2.5f, 1.5f), "RotBuildTissue", 0), Is.False);
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).NetworkReady, Is.True);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Connected.Contains(new Vector2i(40, 0)), Is.True);
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.Connected.Contains(new Vector2i(2, 1)), Is.True);
            Assert.That(state.Projects, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }
}
