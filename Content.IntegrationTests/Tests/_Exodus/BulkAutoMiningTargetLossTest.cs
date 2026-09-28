using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Destructible.Thresholds;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Exodus;

// Arrange transient beam and scheduler state to exercise each target-loss path deterministically.
#pragma warning disable RA0002

[TestFixture]
public sealed class BulkAutoMiningTargetLossTest
{
    public enum TargetCheck : byte
    {
        Range,
        Beam,
        Search,
    }

    [TestCase(TargetCheck.Range, false)]
    [TestCase(TargetCheck.Range, true)]
    [TestCase(TargetCheck.Beam, false)]
    [TestCase(TargetCheck.Beam, true)]
    [TestCase(TargetCheck.Search, false)]
    [TestCase(TargetCheck.Search, true)]
    public async Task LosingOneTargetPreservesMiningProgressAndCooldowns(TargetCheck check, bool lostFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair);
        var em = pair.Server.EntMan;
        var processed = 0;
        var expectedTotal = 0;

        await pair.Server.WaitAssertion(() =>
        {
            Start(em, setup, lostFirst);
            var console = setup.Console;
            var job = em.GetComponent<BulkAutoMiningJobComponent>(console);
            var lostJob = job.GridJobs[lostFirst ? 0 : 1];
            var survivingJob = job.GridJobs[lostFirst ? 1 : 0];
            AimAt(setup.Emitters[0], lostJob);
            AimAt(setup.Emitters[1], survivingJob);
            var survivingTile = setup.Emitters[1].Comp.BeamTile;
            var firstCooldown = setup.Emitters[0].Comp.NextMiningTime;
            var secondCooldown = setup.Emitters[1].Comp.NextMiningTime;
            processed = console.Comp.ProcessedTiles;
            expectedTotal = console.Comp.TotalTiles - lostJob.RemainingTiles.Count;
            Assert.That(GetMetal(em, setup), Is.EqualTo(processed * 10));

            em.DeleteEntity(setup.Targets[0]);
            if (check == TargetCheck.Search)
            {
                setup.Emitters[0].Comp.BeamGrid = null;
                setup.Emitters[0].Comp.NextTargetSearchTime = TimeSpan.Zero;
                job.NextGridIndex = lostFirst ? 0 : 1;
            }

            // A range witness on another target must not hide the lost target from validation.
            Step(em, console, check == TargetCheck.Range);
            Assert.Multiple(() =>
            {
                Assert.That(console.Comp.Active, Is.True);
                Assert.That(console.Comp.SelectedGrids, Is.EquivalentTo(new[] { setup.Targets[1].Owner }));
                Assert.That(console.Comp.TotalTiles, Is.EqualTo(expectedTotal));
                Assert.That(console.Comp.ProcessedTiles, Is.EqualTo(processed));
                Assert.That(GetMetal(em, setup), Is.EqualTo(processed * 10), "Losing a target cannot pay out or bypass the cooldown.");
                Assert.That(job.GridJobs.Count, Is.EqualTo(2));
                Assert.That(lostJob.Invalidated, Is.True);
                Assert.That(lostJob.RemainingTiles, Is.Empty);
                Assert.That(lostJob.Tiles, Is.Empty);
                Assert.That(lostJob.RangeTiles, Is.Empty);
                Assert.That(lostJob.RangeSearches, Is.Empty);
                Assert.That(setup.Emitters[0].Comp.NextMiningTime, Is.EqualTo(firstCooldown));
                Assert.That(setup.Emitters[1].Comp.NextMiningTime, Is.EqualTo(secondCooldown));
                Assert.That(setup.Emitters[1].Comp.BeamGrid, Is.EqualTo(setup.Targets[1].Owner));
                Assert.That(setup.Emitters[1].Comp.BeamTile, Is.EqualTo(survivingTile));
            });

            Step(em, console, false);
            Assert.That(setup.Emitters[0].Comp.BeamGrid, Is.EqualTo(setup.Targets[1].Owner));
            Step(em, console, true);
            Assert.That(console.Comp.TotalTiles, Is.EqualTo(expectedTotal), "Invalidation must be idempotent.");
            Assert.That(GetMetal(em, setup), Is.EqualTo(processed * 10));

            // Resume normal scheduling to verify actual excavation after the original cooldown.
            job.NextProcessTime = TimeSpan.Zero;
            job.NextRangeCheckTime = TimeSpan.Zero;
        });

        await PoolManager.WaitUntil(pair.Server, () => setup.Console.Comp.ProcessedTiles > processed, maxTicks: 400, tickStep: 5);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(setup.Console.Comp.Active, Is.True);
            Assert.That(GetMetal(em, setup), Is.EqualTo(setup.Console.Comp.ProcessedTiles * 10));
            Assert.That(setup.Console.Comp.TotalTiles, Is.EqualTo(expectedTotal));
            var maps = em.System<SharedMapSystem>();
            var remaining = 0;
            var tiles = maps.GetAllTilesEnumerator(setup.Targets[1], setup.Targets[1].Comp);
            while (tiles.MoveNext(out var tile))
            {
                if (tile is { } tileRef && !tileRef.Tile.IsEmpty)
                    remaining++;
            }

            Assert.That(remaining, Is.EqualTo(expectedTotal - setup.Console.Comp.ProcessedTiles));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StopsWhenRemainingTargetsAreLostOrOutOfRange(bool survivorOutOfRange)
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            Start(em, setup);
            var metal = GetMetal(em, setup);
            em.DeleteEntity(setup.Targets[0]);
            if (survivorOutOfRange)
                em.System<SharedTransformSystem>().SetWorldPosition(setup.Targets[1], new Vector2(200, 0));
            else
                em.QueueDeleteEntity(setup.Targets[1]);

            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.False);
            Assert.That(GetMetal(em, setup), Is.EqualTo(metal));
            foreach (var emitter in setup.Emitters)
            {
                Assert.That(emitter.Comp.Controller, Is.Null);
                Assert.That(emitter.Comp.BeamGrid, Is.Null);
            }

            if (!survivorOutOfRange)
                Assert.That(setup.Console.Comp.TotalTiles, Is.EqualTo(setup.Console.Comp.ProcessedTiles));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TargetsCanLeaveAndReenterRangeWhileAnotherRemainsAvailable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            Start(em, setup);
            var transform = em.System<SharedTransformSystem>();
            transform.SetWorldPosition(setup.Targets[0], new Vector2(200, 0));
            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.True);
            Assert.That(setup.Console.Comp.SelectedGrids.Count, Is.EqualTo(2));

            transform.SetWorldPosition(setup.Targets[0], new Vector2(20, 0));
            transform.SetWorldPosition(setup.Targets[1], new Vector2(200, 10));
            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.True, "An out-of-range target must remain eligible to return.");

            transform.SetWorldPosition(setup.Targets[0], new Vector2(200, 0));
            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExhaustedGridDoesNotInvalidateOtherTargets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair, firstTiles: 1);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            Start(em, setup);
            var job = em.GetComponent<BulkAutoMiningJobComponent>(setup.Console);
            Assert.That(job.GridJobs[0].RemainingTiles, Is.Empty);
            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.True);
            Assert.That(job.GridJobs[0].Invalidated, Is.False);
            Assert.That(setup.Console.Comp.TotalTiles, Is.EqualTo(17));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LosingTargetPreservesBoundedSearchUntilReachableTileIsFound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair, secondTiles: 256);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            Start(em, setup);
            var job = em.GetComponent<BulkAutoMiningJobComponent>(setup.Console);
            var survivor = job.GridJobs[1];
            survivor.RangeTiles.Sort((a, b) => b.X.CompareTo(a.X));
            Array.Clear(survivor.RangeSearches);
            foreach (var emitter in setup.Emitters)
                emitter.Comp.BeamGrid = null;

            em.DeleteEntity(setup.Targets[0]);
            Step(em, setup.Console, true);
            var checkedTiles = 0;
            foreach (var search in survivor.RangeSearches)
                checkedTiles += search.NextTileIndex;

            Assert.That(checkedTiles, Is.EqualTo(64), "All lasers share the existing range-search budget.");
            Assert.That(setup.Console.Comp.Active, Is.True, "An unfinished search is not proof that no target is reachable.");

            var found = false;
            for (var i = 0; i < 16 && !found; i++)
            {
                Step(em, setup.Console, true);
                Assert.That(setup.Console.Comp.Active, Is.True);
                foreach (var search in survivor.RangeSearches)
                    found |= search.CachedTile.HasValue;
            }

            Assert.That(found, Is.True);
            em.DeleteEntity(setup.Targets[1]);
            Step(em, setup.Console, true);
            Assert.That(setup.Console.Comp.Active, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwoLasersCannotBothClaimTheLastNaturalTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var setup = await CreateSetup(pair, firstTiles: 1);
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<BulkAutoMiningSystem>();
            Assert.That(system.TrySelectGrid(setup.Console, setup.Targets[0]), Is.True);
            Assert.That(system.TryStartMining(setup.Console), Is.True);
            Assert.That(setup.Console.Comp.Active, Is.False);
            Assert.That(setup.Console.Comp.ProcessedTiles, Is.EqualTo(1));
            Assert.That(GetMetal(em, setup), Is.EqualTo(10));
        });
        await pair.CleanReturnAsync();
    }

    private static async Task<MiningSetup> CreateSetup(TestPair pair, int firstTiles = 16, int secondTiles = 16)
    {
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var setup = new MiningSetup();
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -4; x <= 1; x++)
            {
                for (var y = 0; y <= 6; y++)
                    maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }

            var consoleUid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(map.Grid, -3.5f, .5f));
            setup.Console = (consoleUid, em.GetComponent<BulkAutoMiningConsoleComponent>(consoleUid));
            setup.Console.Comp.MaxRange = 64;
            setup.Console.Comp.TilesPerTick = 1;
            setup.Console.Comp.ProcessInterval = TimeSpan.FromSeconds(10);
            var power = em.System<PowerReceiverSystem>();
            power.SetNeedsPower(consoleUid, false);
            for (var i = 0; i < setup.Emitters.Length; i++)
            {
                var uid = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, .5f + i * 5));
                var emitter = em.GetComponent<BulkAutoMiningEmitterComponent>(uid);
                emitter.SlurryPerTile = new MinMax(10, 10);
                // Target-loss checks require a fixed yield independent of beam warmup.
                emitter.MaxWarmupYieldBonus = 0;
                setup.Emitters[i] = (uid, emitter);
                power.SetNeedsPower(uid, false);
            }

            var manager = pair.Server.ResolveDependency<IMapManager>();
            setup.Targets[0] = CreateTarget(em, manager, map, new Vector2(20, 0), firstTiles);
            setup.Targets[1] = CreateTarget(em, manager, map, new Vector2(20, 10), secondTiles);
        });
        await PoolManager.WaitUntil(pair.Server, () =>
            em.GetComponent<ApcPowerReceiverComponent>(setup.Console).Powered &&
            em.GetComponent<ApcPowerReceiverComponent>(setup.Emitters[0]).Powered &&
            em.GetComponent<ApcPowerReceiverComponent>(setup.Emitters[1]).Powered);
        return setup;
    }

    private static Entity<MapGridComponent> CreateTarget(IEntityManager em, IMapManager manager, TestMapData map, Vector2 position, int tiles)
    {
        var grid = manager.CreateGridEntity(map.MapId);
        grid.Comp.CanSplit = false;
        var maps = em.System<SharedMapSystem>();
        for (var x = 0; x < tiles; x++)
            maps.SetTile(grid, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

        // These synthetic targets represent freshly generated natural deposits.
        em.AddComponent<BulkMiningDepositComponent>(grid);
        var generated = new BulkMiningDepositGeneratedEvent();
        em.EventBus.RaiseLocalEvent(grid, ref generated);

        em.System<SharedTransformSystem>().SetWorldPosition(grid, position);
        return grid;
    }

    private static void Start(IEntityManager em, MiningSetup setup, bool lostFirst = true)
    {
        var system = em.System<BulkAutoMiningSystem>();
        Assert.That(system.TrySelectGrid(setup.Console, setup.Targets[lostFirst ? 0 : 1]), Is.True);
        Assert.That(system.TrySelectGrid(setup.Console, setup.Targets[lostFirst ? 1 : 0]), Is.True);
        Assert.That(system.TryStartMining(setup.Console), Is.True);
        Assert.That(setup.Console.Comp.Active, Is.True);
        Assert.That(setup.Console.Comp.ProcessedTiles, Is.EqualTo(2));
    }

    private static void AimAt(Entity<BulkAutoMiningEmitterComponent> emitter, BulkAutoMiningGridJob target)
    {
        foreach (var tile in target.RemainingTiles)
        {
            emitter.Comp.BeamGrid = target.GridUid;
            emitter.Comp.BeamTile = tile;
            return;
        }

        Assert.Fail("Expected a remaining tile for the test beam.");
    }

    private static void Step(IEntityManager em, Entity<BulkAutoMiningConsoleComponent> console, bool rangeOnly)
    {
        var job = em.GetComponent<BulkAutoMiningJobComponent>(console);
        job.NextProcessTime = rangeOnly ? TimeSpan.MaxValue : TimeSpan.Zero;
        job.NextRangeCheckTime = rangeOnly ? TimeSpan.Zero : TimeSpan.MaxValue;
        job.NextBeamCheckTime = TimeSpan.MaxValue;
        em.System<BulkAutoMiningSystem>().Update(0f);
    }

    private static int GetMetal(IEntityManager em, MiningSetup setup)
    {
        var materials = em.System<MaterialStorageSystem>();
        var amount = 0;
        foreach (var emitter in setup.Emitters)
            amount += materials.GetTotalMaterialAmount(emitter, localOnly: true);

        return amount;
    }

    private sealed class MiningSetup
    {
        public Entity<BulkAutoMiningConsoleComponent> Console;
        public readonly Entity<BulkAutoMiningEmitterComponent>[] Emitters = new Entity<BulkAutoMiningEmitterComponent>[2];
        public readonly Entity<MapGridComponent>[] Targets = new Entity<MapGridComponent>[2];
    }
}

#pragma warning restore RA0002
