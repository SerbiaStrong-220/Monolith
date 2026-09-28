using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server._NF.StationEvents.Components;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Procedural;
using Content.Server.Worldgen.Components;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Damage;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

// Arrange saved deposits and scheduler state to exercise construction and concurrent miners.
#pragma warning disable RA0002

[TestFixture]
public sealed class BulkMiningDepositTest
{
    [TestCase("AsteroidDebrisSmall")]
    [TestCase("NFAsteroidDebrisSmall")]
    [TestCase("NFAsteroidIceDebrisSmall")]
    [TestCase("NFAsteroidAndesiteDebrisSmall")]
    [TestCase("NFAsteroidBasaltDebrisSmall")]
    [TestCase("NFAsteroidSandDebrisSmall")]
    [TestCase("NFAsteroidChromiteDebrisSmall")]
    [TestCase("NFAsteroidRockDebrisSmall")]
    [TestCase("NFAsteroidScrapDebrisSmall")]
    public async Task GeneratedAsteroidsGrantOnlyOriginalTiles(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity(prototype, new MapCoordinates(new Vector2(100, 0), map.MapId));
            em.RemoveComponent<LocalityLoaderComponent>(uid);
            var grid = em.GetComponent<MapGridComponent>(uid);
            grid.CanSplit = false;
            var deposits = em.System<BulkMiningDepositSystem>();
            var maps = em.System<SharedMapSystem>();
            Assert.That(deposits.IsInitialized(uid), Is.True);
            var count = 0;
            var tiles = maps.GetAllTilesEnumerator(uid, grid);
            while (tiles.MoveNext(out var tile))
            {
                if (tile is not { } tileRef || tileRef.Tile.IsEmpty)
                    continue;

                Assert.That(deposits.CanMine(uid, tileRef.GridIndices), Is.True);
                count++;
            }

            Assert.That(count, Is.GreaterThan(0));
            var added = new Vector2i(100, -100);
            maps.SetTile(uid, grid, added, map.Tile.Tile);
            Capture(em, uid);
            Assert.That(deposits.CanMine(uid, added), Is.False, "Generation cannot be replayed to grant constructed tiles.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlanetoidConfigurationAndDungeonGenerationGrantDeposits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        Entity<MapGridComponent> grid = default;
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ProtoMan;
            foreach (var id in new[] { "BluespaceDungeonBasalt", "BluespaceDungeonSnow", "BluespaceDungeonCave", "BluespaceDungeonChromite", "BluespaceDungeonScrap" })
            {
                var prototype = prototypes.Index<EntityPrototype>(id);
                Assert.That(prototype.TryGetComponent<BluespaceErrorRuleComponent>(out var rule, em.ComponentFactory), Is.True);
                Assert.That(rule.Groups["vgroid"].AddComponents.ContainsKey("BulkMiningDeposit"), Is.True, id);
            }

            grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            em.AddComponent<BulkMiningDepositComponent>(grid);
            var config = new DungeonConfig
            {
                Layers =
                [
                    new NoiseDistanceDunGen
                    {
                        Size = new Vector2i(8, 8),
                        Layers = [new NoiseDunGenLayer { Tile = "Lattice", Threshold = -2, Noise = new FastNoiseLite() }],
                    },
                ],
            };
            em.System<DungeonSystem>().GenerateDungeon(config, "bulk-mining-test", grid, grid.Comp, Vector2i.Zero, 42);
        });
        await PoolManager.WaitUntil(pair.Server, () => em.System<BulkMiningDepositSystem>().IsInitialized(grid));
        await pair.Server.WaitAssertion(() => Assert.That(em.System<BulkMiningDepositSystem>().CanMine(grid, Vector2i.Zero), Is.True));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReplacementRemovalAndSaveLoadCannotRegenerateDeposits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var deposits = em.System<BulkMiningDepositSystem>();
            var points = new[] { new Vector2i(-9, -9), new Vector2i(-8, -8), new Vector2i(-1, -1), new Vector2i(7, 7), new Vector2i(8, 8) };
            map.Grid.Comp.CanSplit = false;
            foreach (var point in points)
                maps.SetTile(map.Grid, map.Grid.Comp, point, map.Tile.Tile);

            em.AddComponent<BulkMiningDepositComponent>(map.Grid);
            Capture(em, map.Grid);
            var lattice = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            maps.SetTiles(map.Grid, map.Grid.Comp,
            [
                (points[0], Tile.Empty),
                (points[1], lattice),
                (points[2], new Tile(map.Tile.Tile.TypeId, variant: 1)),
                (new Vector2i(40, 40), lattice),
            ]);
            maps.SetTile(map.Grid, map.Grid.Comp, points[0], map.Tile.Tile);
            maps.SetTile(map.Grid, map.Grid.Comp, points[1], map.Tile.Tile);
            Capture(em, map.Grid);
            Assert.That(deposits.CanMine(map.Grid, points[0]), Is.False);
            Assert.That(deposits.CanMine(map.Grid, points[1]), Is.False);
            Assert.That(deposits.CanMine(map.Grid, points[2]), Is.True, "A cosmetic tile variant is not new flooring.");
            Assert.That(deposits.CanMine(map.Grid, points[3]), Is.True);
            Assert.That(deposits.CanMine(map.Grid, points[4]), Is.True);
            Assert.That(deposits.CanMine(map.Grid, new Vector2i(40, 40)), Is.False);

            var loader = em.System<MapLoaderSystem>();
            using var text = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid, text), Is.True);
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(text.ToString());
            Assert.That(loader.TryLoadGrid(reader, "bulk-mining-deposit-save", out _, out var loaded,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var uid = loaded!.Value.Owner;
            Capture(em, uid);
            Assert.That(deposits.CanMine(uid, points[0]), Is.False);
            Assert.That(deposits.CanMine(uid, points[1]), Is.False);
            Assert.That(deposits.CanMine(uid, points[2]), Is.True);
            Assert.That(deposits.CanMine(uid, points[3]), Is.True);
            Assert.That(deposits.CanMine(uid, points[4]), Is.True);
            Assert.That(deposits.CanMine(uid, new Vector2i(40, 40)), Is.False);
            // Loading created a separate map; do not leak this deposit into the next pooled test.
            em.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SplittingTransfersOnlyRemainingOriginalTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var deposits = em.System<BulkMiningDepositSystem>();
            var grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            grid.Comp.CanSplit = false;
            for (var x = -4; x <= 4; x++)
                maps.SetTile(grid, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            em.AddComponent<BulkMiningDepositComponent>(grid);
            Capture(em, grid);
            maps.SetTile(grid, grid.Comp, new Vector2i(-5, 0), map.Tile.Tile);
            maps.SetTile(grid, grid.Comp, new Vector2i(-2, 0), Tile.Empty);
            maps.SetTile(grid, grid.Comp, new Vector2i(-2, 0), map.Tile.Tile);
            maps.SetTile(grid, grid.Comp, Vector2i.Zero, Tile.Empty);
            grid.Comp.CanSplit = true;
            em.System<GridFixtureSystem>().CheckSplits(grid);

            var found = 0;
            var rights = 0;
            var query = em.EntityQueryEnumerator<BulkMiningDepositComponent, MapGridComponent>();
            while (query.MoveNext(out var uid, out _, out var fragment))
            {
                found++;
                Assert.That(deposits.IsInitialized(uid), Is.True);
                var tiles = maps.GetAllTilesEnumerator(uid, fragment);
                while (tiles.MoveNext(out var tile))
                {
                    if (tile is not { } tileRef || tileRef.Tile.IsEmpty)
                        continue;

                    var expected = tileRef.GridIndices.X is not (-5 or -2);
                    Assert.That(deposits.CanMine(uid, tileRef.GridIndices), Is.EqualTo(expected));
                    if (expected)
                        rights++;
                }
            }

            Assert.That(found, Is.EqualTo(2));
            Assert.That(rights, Is.EqualTo(7));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, "none")]
    [TestCase(false, "station")]
    [TestCase(false, "shuttle")]
    [TestCase(false, "readonly")]
    [TestCase(true, "none")]
    [TestCase(true, "station")]
    [TestCase(true, "shuttle")]
    [TestCase(true, "readonly")]
    public async Task MiningUsesOnlyOriginalDepositsRegardlessOfGridFlags(bool natural, string flags)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        Entity<BulkAutoMiningConsoleComponent> console = default;
        Entity<BulkAutoMiningEmitterComponent> emitter = default;
        Entity<MapGridComponent> target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -4; x <= 1; x++)
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var consoleUid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(map.Grid, -3.5f, .5f));
            console = (consoleUid, em.GetComponent<BulkAutoMiningConsoleComponent>(consoleUid));
            console.Comp.MaxRange = 64;
            console.Comp.TilesPerTick = 1;
            console.Comp.ProcessInterval = TimeSpan.FromSeconds(10);
            var emitterUid = em.SpawnEntity("BulkAutoMiningEmitter", map.GridCoords);
            emitter = (emitterUid, em.GetComponent<BulkAutoMiningEmitterComponent>(emitterUid));
            emitter.Comp.SlurryPerTile = new MinMax(10, 10);
            // Deposit accounting requires a fixed yield independent of beam warmup.
            emitter.Comp.MaxWarmupYieldBonus = 0;
            emitter.Comp.ForbiddenTileDamage = new DamageSpecifier { DamageDict = new() { ["Heat"] = 1 } };
            var power = em.System<PowerReceiverSystem>();
            power.SetNeedsPower(console, false);
            power.SetNeedsPower(emitter, false);

            target = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            target.Comp.CanSplit = false;
            em.System<SharedTransformSystem>().SetWorldPosition(target, new Vector2(20, 0));
            var lattice = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            for (var x = 0; x < 4; x++)
                maps.SetTile(target, target.Comp, new Vector2i(x, 0), lattice);

            if (natural)
            {
                em.AddComponent<BulkMiningDepositComponent>(target);
                Capture(em, target);
                maps.SetTile(target, target.Comp, new Vector2i(4, 0), lattice);
            }
        });
        await PoolManager.WaitUntil(pair.Server, () =>
            em.GetComponent<ApcPowerReceiverComponent>(console).Powered && em.GetComponent<ApcPowerReceiverComponent>(emitter).Powered);
        await pair.Server.WaitAssertion(() =>
        {
            var mining = em.System<BulkAutoMiningSystem>();
            var maps = em.System<SharedMapSystem>();
            var materials = em.System<MaterialStorageSystem>();
            var deposits = em.System<BulkMiningDepositSystem>();
            Assert.That(mining.TrySelectGrid(console, target), Is.True);
            // Legacy grid classification must not affect eligibility, even if it changes during a job.
            if (flags == "station")
                em.AddComponent<StationMemberComponent>(target);
            else if (flags == "shuttle")
                em.EnsureComponent<IFFComponent>(target).Flags |= IFFFlags.IsPlayerShuttle;
            else if (flags == "readonly")
                em.EnsureComponent<IFFComponent>(target).ReadOnly = true;

            mining.TryStartMining(console);
            if (!natural)
            {
                Assert.That(console.Comp.Active, Is.False);
                Assert.That(materials.GetTotalMaterialAmount(emitter, localOnly: true), Is.Zero);
                Assert.That(em.GetComponent<DamageableComponent>(emitter).TotalDamage.Float(), Is.EqualTo(1));
                Assert.That(maps.GetTileRef(target, target.Comp, Vector2i.Zero).Tile.IsEmpty, Is.False);
                return;
            }

            Assert.That(materials.GetTotalMaterialAmount(emitter, localOnly: true), Is.EqualTo(10));
            var first = emitter.Comp.BeamTile;
            // Replace all remaining original tiles while the beam and work queue are cached.
            for (var x = 0; x < 4; x++)
            {
                var point = new Vector2i(x, 0);
                maps.SetTile(target, target.Comp, point, Tile.Empty);
                maps.SetTile(target, target.Comp, point, map.Tile.Tile);
                Assert.That(deposits.CanMine(target, point), Is.False);
            }

            var job = em.GetComponent<BulkAutoMiningJobComponent>(console);
            job.NextProcessTime = TimeSpan.Zero;
            job.NextRangeCheckTime = TimeSpan.Zero;
            emitter.Comp.NextMiningTime = TimeSpan.Zero;
            mining.Update(0);
            Assert.That(materials.GetTotalMaterialAmount(emitter, localOnly: true), Is.EqualTo(10));
            Assert.That(console.Comp.Active, Is.False);
            Assert.That(maps.GetTileRef(target, target.Comp, first).Tile.IsEmpty, Is.False);
            Assert.That(em.GetComponent<DamageableComponent>(emitter).TotalDamage.Float(), Is.Zero);
            Assert.That(mining.TryStartMining(console), Is.False, "Restarting cannot capture constructed replacements.");
        });
        await pair.CleanReturnAsync();
    }

    private static void Capture(IEntityManager em, EntityUid grid)
    {
        var generated = new BulkMiningDepositGeneratedEvent();
        em.EventBus.RaiseLocalEvent(grid, ref generated);
    }
}

#pragma warning restore RA0002
