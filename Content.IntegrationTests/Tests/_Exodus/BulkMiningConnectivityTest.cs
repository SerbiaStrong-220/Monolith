using System.Collections.Generic;
using Content.Server._Exodus.Mining.AutoMining;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

// Inspect transient analysis state to verify shared caches and bounded work.
#pragma warning disable RA0002

[TestFixture]
public sealed partial class BulkMiningConnectivityTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SafetyMatchesIndependentFloodFill(bool largeShapes)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var manager = pair.Server.ResolveDependency<IMapManager>();
            var maps = em.System<SharedMapSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var random = new Random(220);
            for (var sample = 1; sample < (largeShapes ? 65 : 512); sample++)
            {
                var points = new HashSet<Vector2i>();
                if (largeShapes)
                {
                    // Connected growth across positive/negative chunk boundaries, with loops and branches.
                    var point = new Vector2i(-16, -16);
                    points.Add(point);
                    while (points.Count < 96)
                    {
                        point = Adjacent(point, random.Next(4));
                        points.Add(point);
                    }
                }
                else
                {
                    for (var bit = 0; bit < 9; bit++)
                    {
                        if ((sample & (1 << bit)) != 0)
                            points.Add(new Vector2i(bit % 3 - 1, bit / 3 - 1));
                    }

                    if (!IsConnected(points))
                        continue;
                }

                var grid = manager.CreateGridEntity(map.MapId);
                grid.Comp.CanSplit = false;
                var changes = new List<(Vector2i, Tile)>();
                foreach (var point in points)
                    changes.Add((point, map.Tile.Tile));

                maps.SetTiles(grid, grid.Comp, changes);
                connectivity.RetainGrid(map.MapUid, grid);
                foreach (var point in points)
                {
                    var expected = IsConnected(points, point) ? BulkMiningTileSafety.Safe : BulkMiningTileSafety.Unsafe;
                    Assert.That(ResolveSafety(connectivity, grid, point), Is.EqualTo(expected), $"Shape {sample}, tile {point}");
                }

                em.DeleteEntity(grid);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeometryChangesInvalidateAllMinersBeforeTheirNextCut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            grid.Comp.CanSplit = false;
            var maps = em.System<SharedMapSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var changes = new List<(Vector2i, Tile)>();
            for (var x = 0; x <= 40; x++)
            {
                for (var y = 0; y <= 6; y++)
                {
                    if (x == 0 || x == 40 || y == 0 || y == 6)
                        changes.Add((new Vector2i(x, y), map.Tile.Tile));
                }
            }

            maps.SetTiles(grid, grid.Comp, changes);
            connectivity.RetainGrid(map.MapUid, grid);
            connectivity.RetainGrid(map.Grid, grid);
            var cache = em.GetComponent<BulkMiningConnectivityComponent>(grid);
            Assert.That(cache.Users.Count, Is.EqualTo(2));
            var first = new Vector2i(0, 3);
            var second = new Vector2i(40, 3);
            Assert.That(ResolveSafety(connectivity, grid, first), Is.EqualTo(BulkMiningTileSafety.Safe));
            Assert.That(connectivity.GetTileSafety(grid, second), Is.EqualTo(BulkMiningTileSafety.Safe));

            // Each cut is safe separately. Together they would split this hollow asteroid in two.
            maps.SetTile(grid, grid.Comp, first, Tile.Empty);
            Assert.That(connectivity.GetTileSafety(grid, second), Is.EqualTo(BulkMiningTileSafety.Pending));
            Assert.That(ResolveSafety(connectivity, grid, second), Is.EqualTo(BulkMiningTileSafety.Unsafe));
            Assert.That(connectivity.GetTileSafety(grid, first), Is.EqualTo(BulkMiningTileSafety.Unsafe));

            // A constructed connection counts too: connectivity follows all floor tiles, not deposit rights.
            maps.SetTile(grid, grid.Comp, first, map.Tile.Tile);
            Assert.That(ResolveSafety(connectivity, grid, second), Is.EqualTo(BulkMiningTileSafety.Safe));
            maps.SetTile(grid, grid.Comp, first, new Tile(map.Tile.Tile.TypeId, variant: 1));
            Assert.That(cache.Complete, Is.True, "Cosmetic tile changes must preserve the analysis.");

            connectivity.ReleaseGrid(map.MapUid, grid);
            Assert.That(cache.Users.Count, Is.EqualTo(1));
            Assert.That(connectivity.GetTileSafety(grid, second), Is.EqualTo(BulkMiningTileSafety.Safe));
            connectivity.ReleaseGrid(map.Grid, grid);
            connectivity.RetainGrid(map.MapUid, grid);
            Assert.That(em.GetComponent<BulkMiningConnectivityComponent>(grid).Users.Count, Is.EqualTo(1),
                "Stopping and restarting in the same tick must retain a live cache.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LargeAnalysesShareABoundedBudgetAndDiscardPartialResults()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var manager = pair.Server.ResolveDependency<IMapManager>();
            var maps = em.System<SharedMapSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var grids = new Entity<MapGridComponent>[2];
            var caches = new BulkMiningConnectivityComponent[2];
            for (var i = 0; i < grids.Length; i++)
            {
                var grid = manager.CreateGridEntity(map.MapId);
                grid.Comp.CanSplit = false;
                maps.SetTiles(grid, grid.Comp, Dumbbell(map.Tile.Tile, 64));
                connectivity.RetainGrid(map.MapUid, grid);
                grids[i] = grid;
                caches[i] = em.GetComponent<BulkMiningConnectivityComponent>(grid);
                Assert.That(connectivity.GetTileSafety(grid, Vector2i.Zero), Is.EqualTo(BulkMiningTileSafety.Pending));
            }

            connectivity.Update(0);
            Assert.That(caches[0].Discovered + caches[1].Discovered, Is.InRange(3, 16386));
            Assert.That(caches[0].Complete || caches[1].Complete, Is.False,
                "An 8193-tile graph cannot be traversed synchronously in a single update.");

            // Alter the first grid while its DFS stack is unfinished.
            var generation = caches[0].Generation;
            maps.SetTile(grids[0], grids[0].Comp, new Vector2i(-64, 63), Tile.Empty);
            Assert.That(caches[0].Pending, Is.False);
            Assert.That(caches[0].Complete, Is.False);
            Assert.That(connectivity.GetTileSafety(grids[0], Vector2i.Zero), Is.EqualTo(BulkMiningTileSafety.Pending));
            Assert.That(caches[0].Generation, Is.GreaterThan(generation));

            for (var step = 0; step < 2048 && (caches[0].Pending || caches[1].Pending); step++)
            {
                var before = caches[0].Discovered + caches[1].Discovered;
                connectivity.Update(0);
                Assert.That(caches[0].Discovered + caches[1].Discovered - before, Is.LessThanOrEqualTo(16384));
            }

            foreach (var grid in grids)
                Assert.That(connectivity.GetTileSafety(grid, Vector2i.Zero), Is.EqualTo(BulkMiningTileSafety.Unsafe));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeafCutsPreserveResultsAndUpdateTheirNeighbors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            grid.Comp.CanSplit = false;
            var maps = em.System<SharedMapSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            for (var x = -32; x <= 32; x++)
                maps.SetTile(grid, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            connectivity.RetainGrid(map.MapUid, grid);
            Assert.That(ResolveSafety(connectivity, grid, Vector2i.Zero), Is.EqualTo(BulkMiningTileSafety.Unsafe));
            var cache = em.GetComponent<BulkMiningConnectivityComponent>(grid);
            var generation = cache.Generation;
            // Peel both ends until only the original DFS root remains.
            for (var distance = 32; distance > 0; distance--)
            {
                foreach (var x in new[] { -distance, distance })
                {
                    var leaf = new Vector2i(x, 0);
                    Assert.That(connectivity.GetTileSafety(grid, leaf), Is.EqualTo(BulkMiningTileSafety.Safe));
                    maps.SetTile(grid, grid.Comp, leaf, Tile.Empty);
                    Assert.That(cache.Complete, Is.True);
                    Assert.That(cache.Generation, Is.EqualTo(generation));
                    Assert.That(connectivity.GetTileSafety(grid, leaf), Is.EqualTo(BulkMiningTileSafety.Unsafe));
                }

                Assert.That(connectivity.GetTileSafety(grid, new Vector2i(1 - distance, 0)), Is.EqualTo(BulkMiningTileSafety.Safe));
                Assert.That(connectivity.GetTileSafety(grid, Vector2i.Zero),
                    Is.EqualTo(distance > 1 ? BulkMiningTileSafety.Unsafe : BulkMiningTileSafety.Safe));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CachedGraphMatchesFloodFillAfterMiningConstructionAndBatchEdits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var manager = pair.Server.ResolveDependency<IMapManager>();
            var maps = em.System<SharedMapSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var random = new Random(2202026);
            for (var sample = 0; sample < 16; sample++)
            {
                var grid = manager.CreateGridEntity(map.MapId);
                grid.Comp.CanSplit = false;
                var points = new HashSet<Vector2i> { new(-16, -16) };
                var point = new Vector2i(-16, -16);
                while (points.Count < 48)
                {
                    point = Adjacent(point, random.Next(4));
                    points.Add(point);
                }

                var changes = new List<(Vector2i, Tile)>();
                foreach (var tile in points)
                    changes.Add((tile, map.Tile.Tile));

                maps.SetTiles(grid, grid.Comp, changes);
                connectivity.RetainGrid(map.MapUid, grid);
                var candidates = new List<Vector2i>();
                while (points.Count > 1)
                {
                    candidates.Clear();
                    foreach (var tile in points)
                    {
                        var safe = IsConnected(points, tile);
                        Assert.That(ResolveSafety(connectivity, grid, tile),
                            Is.EqualTo(safe ? BulkMiningTileSafety.Safe : BulkMiningTileSafety.Unsafe),
                            $"Shape {sample}, {points.Count} tiles remain, checking {tile}");
                        if (safe)
                            candidates.Add(tile);
                    }

                    var removed = candidates[random.Next(candidates.Count)];
                    maps.SetTile(grid, grid.Comp, removed, Tile.Empty);
                    if (points.Count % 7 == 0)
                    {
                        // Reuse the same cached tile indices after construction and a batch invalidation.
                        maps.SetTile(grid, grid.Comp, removed, map.Tile.Tile);
                        maps.SetTiles(grid, grid.Comp,
                        [
                            (removed, Tile.Empty),
                            (new Vector2i(100, 100), map.Tile.Tile),
                        ]);
                        maps.SetTile(grid, grid.Comp, new Vector2i(100, 100), Tile.Empty);
                    }

                    points.Remove(removed);
                }

                em.DeleteEntity(grid);
            }
        });
        await pair.CleanReturnAsync();
    }

    private static BulkMiningTileSafety ResolveSafety(BulkMiningConnectivitySystem system, Entity<MapGridComponent> grid, Vector2i tile)
    {
        for (var step = 0; step < 2048; step++)
        {
            var safety = system.GetTileSafety(grid, tile);
            if (safety != BulkMiningTileSafety.Pending)
                return safety;

            system.Update(0);
        }

        Assert.Fail("The bounded analysis did not finish.");
        return BulkMiningTileSafety.Pending;
    }

    private static bool IsConnected(HashSet<Vector2i> points, Vector2i? removed = null)
    {
        var reached = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        foreach (var point in points)
        {
            if (point == removed)
                continue;

            reached.Add(point);
            queue.Enqueue(point);
            break;
        }

        while (queue.TryDequeue(out var point))
        {
            for (var direction = 0; direction < 4; direction++)
            {
                var next = Adjacent(point, direction);
                if (next != removed && points.Contains(next) && reached.Add(next))
                    queue.Enqueue(next);
            }
        }

        return reached.Count == points.Count - (removed.HasValue && points.Contains(removed.Value) ? 1 : 0);
    }

    private static List<(Vector2i, Tile)> Dumbbell(Tile tile, int size)
    {
        var changes = new List<(Vector2i, Tile)> { (Vector2i.Zero, tile) };
        for (var x = 1; x <= size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                changes.Add((new Vector2i(-x, y), tile));
                changes.Add((new Vector2i(x, y), tile));
            }
        }

        return changes;
    }

    private static Vector2i Adjacent(Vector2i point, int direction)
    {
        return direction switch
        {
            0 => point + new Vector2i(1, 0),
            1 => point + new Vector2i(0, 1),
            2 => point + new Vector2i(-1, 0),
            _ => point + new Vector2i(0, -1),
        };
    }
}

#pragma warning restore RA0002
