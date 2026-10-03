using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Worldgen.Components;
using Content.Shared._Exodus.Teleport;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class NearestShuttleTeleporterTest
{
    [TestCase(BodyType.Dynamic)]
    [TestCase(BodyType.Static)]
    public async Task SkipsCloserAsteroid(BodyType destinationBodyType)
    {
        await VerifyDestination(destinationBodyType);
    }

    [Test]
    public async Task OnlyAsteroidLeavesUserOnPad()
    {
        await VerifyDestination(null);
    }

    private static async Task VerifyDestination(BodyType? destinationBodyType)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var transform = em.System<SharedTransformSystem>();
            var coords = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var pad = em.SpawnEntity("NearestShuttleTeleporterPad", coords);
            var user = em.SpawnEntity("MobHuman", coords);

            var asteroid = em.SpawnEntity("NFAsteroidDebrisSmall", new MapCoordinates(20, 0, map.MapId));
            // Keep the generated floor, but prevent deferred rock population from blocking it.
            em.RemoveComponent<LocalityLoaderComponent>(asteroid);
            var asteroidGrid = em.GetComponent<MapGridComponent>(asteroid);
            var asteroidTile = maps.GetTileRef(asteroid, asteroidGrid, Vector2i.Zero);
            var turf = em.System<TurfSystem>();
            Assert.That(em.HasComponent<ShuttleComponent>(asteroid), Is.True);
            Assert.That(turf.IsSpace(asteroidTile), Is.False);
            Assert.That(turf.IsTileBlocked(asteroidTile, CollisionGroup.MobMask), Is.False);

            EntityUid expectedGrid = map.Grid;
            if (destinationBodyType is { } bodyType)
            {
                var destination = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
                maps.SetTile(destination, destination.Comp, Vector2i.Zero, map.Tile.Tile);
                transform.SetWorldPosition(destination, new Vector2(100, 0));
                em.System<SharedPhysicsSystem>().SetBodyType(destination, bodyType);
                expectedGrid = destination;
            }

            var teleporter = em.GetComponent<NearestShuttleTeleporterComponent>(pad);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var activate = new ActivateInWorldEvent(user, pad, true);
            em.EventBus.RaiseLocalEvent(pad, activate);

            Assert.That(activate.Handled, Is.True);
            Assert.That(em.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(expectedGrid));
            var cooldown = destinationBodyType.HasValue ? teleporter.Cooldown : teleporter.FailureCooldown;
            Assert.That(teleporter.NextUse, Is.EqualTo(now + cooldown));
        });
        await pair.CleanReturnAsync();
    }
}
