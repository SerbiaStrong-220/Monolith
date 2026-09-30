using System;
using System.Collections.Generic;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.SubFloor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [TestCase("FloorSteel")]
    [TestCase("Plating")]
    public async Task UtilitiesDoNotBlockTissueGrowth(string floor)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap(true, floor);
        var utilities = new List<EntityUid>();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -3; x <= 3; x++)
                for (var y = -3; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            // Central's floors contain overlapping cable, gas and disposal networks.
            foreach (var prototype in new[] { "CableHV", "CableMV", "GasPipeStraight", "DisposalPipe" })
            {
                var utility = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                Assert.That(em.GetComponent<SubFloorHideComponent>(utility).IsUnderCover, Is.EqualTo(floor == "FloorSteel"));
                utilities.Add(utility);
            }
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
        });
        await pair.RunSeconds(5.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.Count, Is.GreaterThan(9),
                    "Infrastructure under the core must not trap growth in the starting patch.");
                foreach (var utility in utilities)
                    Assert.That(em.GetComponent<DamageableComponent>(utility).TotalDamage.Float(), Is.Zero,
                        "Surface growth must leave cables and pipes intact, including on exposed plating.");
            });
            var replacement = server.ResolveDependency<ITileDefinitionManager>()[floor == "FloorSteel" ? "Plating" : "FloorSteel"];
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, Vector2i.Zero, new Tile(replacement.TileId));
            foreach (var utility in utilities)
                Assert.That(em.GetComponent<SubFloorHideComponent>(utility).IsUnderCover, Is.EqualTo(floor != "FloorSteel"));
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.Count, Is.GreaterThan(10),
                    "Adding or removing floor tiles must not interrupt growth over utilities.");
                foreach (var utility in utilities)
                    Assert.That(em.GetComponent<DamageableComponent>(utility).TotalDamage.Float(), Is.Zero,
                        "Changing the floor must not make utilities a corrosion target.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task CoreAndOrganSpreadAtTheirDefaultInterval(bool organ, bool controlled)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = controlled }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid source = default;
        EntityUid grown = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 8; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            source = core;
            if (organ)
            {
                em.GetComponent<RotSpreadComponent>(core).NextGrowth += TimeSpan.FromMinutes(1);
                source = em.SpawnEntity("RotEyeball", new EntityCoordinates(map.Grid, 1.5f, .5f));
                Assert.That(em.System<RotIntelligentSystem>().Join(source, core), Is.True);
            }
            if (controlled)
                server.PlayerMan.SetAttachedEntity(pair.Player!, core);
        });
        await pair.RunSeconds(5.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<RotIntelligentSystem>().IsActiveCore(core), Is.True);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(source).Connected, Is.True);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.ContainsKey(new Vector2i(2, 0)), Is.True,
                "A connected source must grow beyond the initial tissue using the configured interval.");
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.Cells.ContainsKey(new Vector2i(3, 0)), Is.True,
                "Rebuilding connectivity after the first new tile must not stop subsequent growth.");
            foreach (var member in state.Members)
            {
                if (em.GetComponent<RotColonyMemberComponent>(member).Tissue
                    && em.GetComponent<TransformComponent>(member).LocalPosition.X == 3.5f)
                    grown = member;
            }
            Assert.That(grown, Is.Not.EqualTo(default(EntityUid)));
        });
        if (controlled)
        {
            await pair.Client.WaitAssertion(() =>
            {
                var client = pair.Client.EntMan;
                var tissue = pair.ToClientUid(grown);
                Assert.That(client.GetComponent<RotColonyMemberComponent>(tissue).Connected, Is.True);
                Assert.That(client.System<Robust.Client.GameObjects.SpriteSystem>().LayerGetRsiState(tissue, 0).ToString(),
                    Is.EqualTo("8"), "The player must receive the grown tissue and its connection sprite.");
            });
        }
        await pair.CleanReturnAsync();
    }
}
