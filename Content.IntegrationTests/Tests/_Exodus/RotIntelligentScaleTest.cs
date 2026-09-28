using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task EightyGridsKeepVisionPrivateAndReuseUnchangedViews()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var cores = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var manager = server.ResolveDependency<IMapManager>();
            var transform = em.System<SharedTransformSystem>();
            var colony = em.System<RotIntelligentSystem>();
            for (var i = 0; i < 80; i++)
            {
                var grid = i == 0 ? map.Grid : manager.CreateGridEntity(map.MapId);
                transform.SetWorldPosition(grid, new Vector2(i * 64, 0));
                for (var x = -2; x <= 2; x++)
                    for (var y = -2; y <= 2; y++)
                        maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(grid, .5f, .5f));
                cores.Add(core);
                colony.Join(em.SpawnEntity("RotEyeball", new EntityCoordinates(grid, 1.5f, 1.5f)), core);
            }
            server.PlayerMan.SetAttachedEntity(pair.Player!, cores[0]);
        });
        await pair.RunSeconds(1);
        uint revision = 0;
        await server.WaitAssertion(() =>
        {
            revision = em.GetComponent<RotColonyStateComponent>(cores[0]).Revision;
            Assert.That(revision, Is.GreaterThan(0));
            for (var i = 1; i < cores.Count; i++)
            {
                Assert.That(em.GetComponent<RotIntelligentComponent>(cores[i]).NetworkReady, Is.True);
                Assert.That(em.GetComponent<RotColonyStateComponent>(cores[i]).Revision, Is.Zero);
            }
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotColonyStateComponent>(cores[0]).Revision, Is.EqualTo(revision));
            Assert.That(em.System<RotIntelligentSystem>().CanSee(cores[0], em.GetComponent<TransformComponent>(cores[79]).Coordinates), Is.False);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var clientCore = pair.ToClientUid(cores[0]);
            var view = pair.Client.EntMan.GetComponent<Content.Client._Exodus.StationAi.CameraViewMaskComponent>(clientCore);
            Assert.That(view.Frame, Is.Not.Null);
            Assert.That(view.Tiles, Is.Not.Empty);
        });
        await pair.CleanReturnAsync();
    }
}
