// Exodus: keep the all-prototype lifecycle test within the CI runner's memory budget.
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

public sealed partial class EntityTest
{
    private const int SpawnMapBatchSize = 128;

    private async Task SpawnAndDeleteEntitiesInMapBatches()
    {
        // Every batch deletes all entities, so this pair must not be reused by another test.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entityMan = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var prototypeMan = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entityMan.System<SharedMapSystem>();
        List<string> protoIds = [];

        await server.WaitPost(() =>
        {
            // Preserve the upstream test's prototype selection.
            protoIds = prototypeMan
                .EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract)
                .Where(p => !pair.IsTestPrototype(p))
                .Where(p => !p.Components.ContainsKey("MapGrid"))
                .Where(p => !p.Components.ContainsKey("RoomFill"))
                .Where(p => p.Categories.All(x => x.ID != SpawnerCategory))
                .Select(p => p.ID)
                .ToList();
        });

        for (var start = 0; start < protoIds.Count; start += SpawnMapBatchSize)
        {
            var end = Math.Min(start + SpawnMapBatchSize, protoIds.Count);
            TestContext.Progress.WriteLine($"{nameof(SpawnAndDeleteAllEntitiesOnDifferentMaps)}: spawning {start + 1}-{end}/{protoIds.Count} ({protoIds[start]} through {protoIds[end - 1]}).");

            await server.WaitPost(() =>
            {
                for (var index = start; index < end; index++)
                {
                    mapSystem.CreateMap(out var mapId);
                    var grid = mapManager.CreateGridEntity(mapId);
                    mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(1));
                    SpawnEntity(entityMan, protoIds[index], new EntityCoordinates(grid.Owner, 0, 0));
                }
            });

            TestContext.Progress.WriteLine($"{nameof(SpawnAndDeleteAllEntitiesOnDifferentMaps)}: running 450 ticks for {start + 1}-{end}/{protoIds.Count}.");
            await server.WaitRunTicks(450);

            await server.WaitPost(() =>
            {
                // Snapshot first: deleting a parent can also delete entities later in the list.
                List<(EntityUid Uid, MetaDataComponent Meta)> entities = [];
                var query = entityMan.AllEntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var uid, out var meta))
                {
                    entities.Add((uid, meta));
                }

                foreach (var (uid, meta) in entities)
                {
                    if (!meta.EntityDeleted)
                        entityMan.DeleteEntity(uid);
                }

                Assert.That(entityMan.EntityCount, Is.Zero, $"Entities remained after prototype batch {start + 1}-{end}/{protoIds.Count}.");
            });

            TestContext.Progress.WriteLine($"{nameof(SpawnAndDeleteAllEntitiesOnDifferentMaps)}: cleaned up {end}/{protoIds.Count} prototypes.");
        }

        await pair.CleanReturnAsync();
    }
}
