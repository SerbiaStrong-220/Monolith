using System.Collections.Generic;
using Content.Server._Exodus.Virology.Lifecycle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class RotColonySiteTest
{
    [Test]
    public async Task HomeIndexFollowsGridChangesAndExcludesPausedAndDeletedSites()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var first = await pair.CreateTestMap();
        var second = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var sites = em.System<RotColonySiteSystem>();
            var transform = em.System<SharedTransformSystem>();
            var searcher = em.SpawnEntity(null, first.GridCoords);
            var home = em.SpawnEntity(null, first.GridCoords);
            em.AddComponent<RotColonySiteComponent>(home);
            var other = em.SpawnEntity(null, second.GridCoords);
            em.AddComponent<RotColonySiteComponent>(other);
            var found = new List<Entity<RotColonySiteComponent, TransformComponent>>();
            sites.GetSites(searcher, found);
            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].Owner, Is.EqualTo(home));
            transform.SetCoordinates(home, second.GridCoords);
            sites.GetSites(searcher, found);
            Assert.That(found, Is.Empty);
            transform.SetCoordinates(searcher, second.GridCoords);
            sites.GetSites(searcher, found);
            Assert.That(found.Count, Is.EqualTo(2));
            em.System<MetaDataSystem>().SetEntityPaused(home, true);
            sites.GetSites(searcher, found);
            Assert.That(found.Count, Is.EqualTo(1));
            em.DeleteEntity(other);
            sites.GetSites(searcher, found);
            Assert.That(found, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }
}
