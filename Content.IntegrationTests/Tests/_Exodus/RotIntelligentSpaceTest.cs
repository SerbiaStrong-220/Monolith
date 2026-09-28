using System.Numerics;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server.NPC.HTN;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task MobileSourceKeepsVisionInSpaceAndOnAnotherGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid mobile = default;
        EntityUid eye = default;
        await server.WaitAssertion(() =>
        {
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            mobile = em.SpawnEntity("MobRotHungry", new EntityCoordinates(map.MapUid, 60.5f, .5f));
            em.RemoveComponent<HTNComponent>(mobile);
            Assert.That(em.System<RotIntelligentSystem>().Join(mobile, core), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
            eye = em.GetComponent<RotIntelligentComponent>(core).Eye!.Value;
            em.System<SharedTransformSystem>().SetCoordinates(eye, new EntityCoordinates(map.MapUid, 60.5f, .5f));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<RotIntelligentSystem>().CanSee(core, new MapCoordinates(63.5f, .5f, map.MapId)), Is.True);
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.ViewFrame, Is.EqualTo(map.MapUid));
            Assert.That(state.LastView, Does.Contain(new Vector2i(63, 0)));
        });
        EntityUid obstructionGrid = default;
        await server.WaitAssertion(() =>
        {
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            obstructionGrid = grid;
            em.System<SharedMapSystem>().SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            em.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(62, 0));
            em.SpawnEntity("WallSolid", new EntityCoordinates(grid, .5f, .5f));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<RotIntelligentSystem>().CanSee(core, new MapCoordinates(63.5f, .5f, map.MapId)), Is.False,
                "A mobile source in space must not see through opaque fixtures on intervening grids.");
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).LastView, Does.Not.Contain(new Vector2i(63, 0)));
            em.QueueDeleteEntity(obstructionGrid);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<RotIntelligentSystem>().CanSee(core, new MapCoordinates(63.5f, .5f, map.MapId)), Is.True);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).LastView, Does.Contain(new Vector2i(63, 0)));
        });
        EntityUid otherGrid = default;
        await server.WaitAssertion(() =>
        {
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            otherGrid = grid;
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 3; x++)
                for (var y = -2; y <= 3; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var transform = em.System<SharedTransformSystem>();
            transform.SetWorldPosition(grid, new Vector2(100, 0));
            transform.SetCoordinates(mobile, new EntityCoordinates(grid, .5f, .5f));
            transform.SetCoordinates(eye, new EntityCoordinates(grid, .5f, .5f));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.ViewFrame, Is.EqualTo(otherGrid));
            Assert.That(state.LastView, Does.Contain(new Vector2i(2, 0)));
            Assert.That(em.System<RotIntelligentSystem>().CanSee(core, new EntityCoordinates(otherGrid, 2.5f, .5f)), Is.True);
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, em.GetComponent<RotIntelligentComponent>(core)),
                new EntityCoordinates(otherGrid, 2.5f, .5f), "RotBuildTissue", 0), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
