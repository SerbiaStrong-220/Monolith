using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Destructible.Thresholds;
using Robust.Server.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

// Arrange beam targets and elapsed cooldowns, while retaining real raycasts, excavation and grid splitting.
#pragma warning disable RA0002

public sealed partial class BulkMiningConnectivityTest
{
    [Test]
    public async Task OccludingBridgeIsSkippedAndTwoLasersFinishWithoutFragments()
    {
        await using var pair = await PoolManager.GetServerClient();
        var points = new HashSet<Vector2i> { new(1, 4) };
        for (var y = 0; y <= 8; y++)
            points.Add(new Vector2i(0, y));

        var setup = await CreateMiningSetup(pair, points, new Vector2(20, -4), 2);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var mining = em.System<BulkAutoMiningSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var maps = em.System<SharedMapSystem>();
            Assert.That(mining.TrySelectGrid(setup.Console, setup.Target), Is.True);
            Assert.That(mining.TryStartMining(setup.Console), Is.True);
            var job = em.GetComponent<BulkAutoMiningJobComponent>(setup.Console);
            var gridJob = job.GridJobs[0];
            var bridge = new Vector2i(0, 4);
            var hiddenLeaf = new Vector2i(1, 4);
            Assert.That(ResolveSafety(connectivity, setup.Target, bridge), Is.EqualTo(BulkMiningTileSafety.Unsafe));
            Assert.That(connectivity.GetTileSafety(setup.Target, hiddenLeaf), Is.EqualTo(BulkMiningTileSafety.Safe));

            // The requested leaf is safe, but its actual beam hit is the dangerous front bridge.
            gridJob.Tiles.Clear();
            gridJob.Tiles.Enqueue(hiddenLeaf);
            foreach (var emitter in setup.Emitters)
            {
                emitter.Comp.BeamGrid = setup.Target;
                emitter.Comp.BeamTile = bridge;
            }

            StepMining(em, setup);
            Assert.That(setup.Console.Comp.ProcessedTiles, Is.Zero);
            Assert.That(GetMetal(em, setup), Is.Zero);
            Assert.That(maps.GetTileRef(setup.Target, setup.Target.Comp, bridge).Tile.IsEmpty, Is.False);
            Assert.That(maps.GetTileRef(setup.Target, setup.Target.Comp, hiddenLeaf).Tile.IsEmpty, Is.False);

            gridJob.Tiles.Clear();
            foreach (var point in points)
                gridJob.Tiles.Enqueue(point);

            for (var step = 0; step < 100 && setup.Console.Comp.Active; step++)
            {
                connectivity.Update(0);
                StepMining(em, setup);
                var remaining = new HashSet<Vector2i>();
                foreach (var point in points)
                {
                    if (!maps.GetTileRef(setup.Target, setup.Target.Comp, point).Tile.IsEmpty)
                        remaining.Add(point);
                }

                Assert.That(IsConnected(remaining), Is.True, "Every excavation must leave one connected body.");
                em.System<GridFixtureSystem>().CheckSplits(setup.Target);
                Assert.That(CountGrids(em, setup.MapUid), Is.EqualTo(remaining.Count == 0 ? 1 : 2),
                    "Only the ship and the remaining asteroid may exist.");
                Assert.That(setup.Console.Comp.SelectedGrids, Is.EqualTo(new[] { setup.Target.Owner }));
                if (setup.Console.Comp.Active)
                    Assert.That(job.GridJobs.Count, Is.EqualTo(1));
            }

            Assert.That(setup.Console.Comp.Active, Is.False);
            Assert.That(setup.Console.Comp.ProcessedTiles, Is.EqualTo(points.Count));
            Assert.That(GetMetal(em, setup), Is.EqualTo(points.Count * 10));
            Assert.That(CountGrids(em, setup.MapUid), Is.EqualTo(1), "After complete excavation only the ship remains.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SecondLaserCannotReuseSafetyBeforeTheFirstCut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var points = new HashSet<Vector2i>();
        for (var x = 0; x <= 8; x++)
        {
            for (var y = 0; y <= 6; y++)
            {
                if (x == 0 || x == 8 || y == 0 || y == 6)
                    points.Add(new Vector2i(x, y));
            }
        }

        // Both lasers can see two different tiles on the near edge of the ring.
        var setup = await CreateMiningSetup(pair, points, new Vector2(20, -3), 2);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var mining = em.System<BulkAutoMiningSystem>();
            var connectivity = em.System<BulkMiningConnectivitySystem>();
            var maps = em.System<SharedMapSystem>();
            Assert.That(mining.TrySelectGrid(setup.Console, setup.Target), Is.True);
            Assert.That(mining.TryStartMining(setup.Console), Is.True);
            var first = new Vector2i(0, 2);
            var second = new Vector2i(0, 4);
            Assert.That(ResolveSafety(connectivity, setup.Target, first), Is.EqualTo(BulkMiningTileSafety.Safe));
            Assert.That(connectivity.GetTileSafety(setup.Target, second), Is.EqualTo(BulkMiningTileSafety.Safe));
            var job = em.GetComponent<BulkAutoMiningJobComponent>(setup.Console);
            // Restrict alternatives so failure to mine the second cut cannot hide behind another safe cut.
            job.GridJobs[0].Tiles.Clear();
            job.GridJobs[0].Tiles.Enqueue(first);
            job.GridJobs[0].Tiles.Enqueue(second);
            setup.Emitters[0].Comp.BeamGrid = setup.Target;
            setup.Emitters[0].Comp.BeamTile = first;
            setup.Emitters[1].Comp.BeamGrid = setup.Target;
            setup.Emitters[1].Comp.BeamTile = second;
            StepMining(em, setup);

            Assert.That(setup.Console.Comp.ProcessedTiles, Is.EqualTo(1));
            Assert.That(GetMetal(em, setup), Is.EqualTo(10));
            Assert.That(maps.GetTileRef(setup.Target, setup.Target.Comp, first).Tile.IsEmpty, Is.True);
            Assert.That(maps.GetTileRef(setup.Target, setup.Target.Comp, second).Tile.IsEmpty, Is.False);
            Assert.That(ResolveSafety(connectivity, setup.Target, second), Is.EqualTo(BulkMiningTileSafety.Unsafe));
            em.System<GridFixtureSystem>().CheckSplits(setup.Target);
            Assert.That(CountGrids(em, setup.MapUid), Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }

    private static async Task<ConnectivityMiningSetup> CreateMiningSetup(TestPair pair, HashSet<Vector2i> points, Vector2 position, int emitterCount)
    {
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var setup = new ConnectivityMiningSetup { MapUid = map.MapUid };
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -4; x <= 1; x++)
            {
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }

            var uid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(map.Grid, -3.5f, .5f));
            setup.Console = (uid, em.GetComponent<BulkAutoMiningConsoleComponent>(uid));
            setup.Console.Comp.MaxRange = 64;
            setup.Console.Comp.TilesPerTick = 1;
            setup.Console.Comp.ProcessInterval = TimeSpan.FromSeconds(10);
            var power = em.System<PowerReceiverSystem>();
            power.SetNeedsPower(uid, false);
            for (var i = 0; i < emitterCount; i++)
            {
                uid = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, -.5f + i * 2));
                var emitter = em.GetComponent<BulkAutoMiningEmitterComponent>(uid);
                emitter.SlurryPerTile = new MinMax(10, 10);
                // Connectivity checks arrange beams directly and require a fixed yield independent of warmup.
                emitter.MaxWarmupYieldBonus = 0;
                emitter.NextMiningTime = TimeSpan.MaxValue;
                setup.Emitters.Add((uid, emitter));
                power.SetNeedsPower(uid, false);
            }

            var grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            setup.Target = grid;
            grid.Comp.CanSplit = false;
            var changes = new List<(Vector2i, Tile)>();
            foreach (var point in points)
                changes.Add((point, map.Tile.Tile));

            maps.SetTiles(grid, grid.Comp, changes);
            grid.Comp.CanSplit = true;
            em.System<SharedTransformSystem>().SetWorldPosition(grid, position);
            foreach (var point in points)
                em.SpawnEntity("AsteroidRock", new EntityCoordinates(grid, point.X + .5f, point.Y + .5f));

            em.AddComponent<BulkMiningDepositComponent>(grid);
            var generated = new BulkMiningDepositGeneratedEvent();
            em.EventBus.RaiseLocalEvent(grid, ref generated);
        });
        await PoolManager.WaitUntil(pair.Server, () =>
        {
            if (!em.GetComponent<ApcPowerReceiverComponent>(setup.Console).Powered)
                return false;

            foreach (var emitter in setup.Emitters)
            {
                if (!em.GetComponent<ApcPowerReceiverComponent>(emitter).Powered)
                    return false;
            }

            return true;
        });
        await pair.RunTicksSync(2);
        return setup;
    }

    private static void StepMining(IEntityManager em, ConnectivityMiningSetup setup)
    {
        var job = em.GetComponent<BulkAutoMiningJobComponent>(setup.Console);
        job.NextProcessTime = TimeSpan.Zero;
        job.NextRangeCheckTime = TimeSpan.MaxValue;
        job.NextBeamCheckTime = TimeSpan.MaxValue;
        foreach (var emitter in setup.Emitters)
        {
            emitter.Comp.NextMiningTime = TimeSpan.Zero;
            emitter.Comp.NextTargetSearchTime = TimeSpan.Zero;
        }

        em.System<BulkAutoMiningSystem>().Update(0);
    }

    private static int CountGrids(IEntityManager em, EntityUid map)
    {
        var count = 0;
        var query = em.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var transform))
        {
            if (transform.MapUid == map)
                count++;
        }

        return count;
    }

    private static int GetMetal(IEntityManager em, ConnectivityMiningSetup setup)
    {
        var total = 0;
        foreach (var emitter in setup.Emitters)
            total += em.System<MaterialStorageSystem>().GetTotalMaterialAmount(emitter, localOnly: true);

        return total;
    }

    private sealed class ConnectivityMiningSetup
    {
        public EntityUid MapUid;
        public Entity<BulkAutoMiningConsoleComponent> Console;
        public Entity<MapGridComponent> Target;
        public readonly List<Entity<BulkAutoMiningEmitterComponent>> Emitters = new();
    }
}

#pragma warning restore RA0002
