#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Destructible.Thresholds;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._Exodus;

// Tunes mining cadence directly, while keeping real target search, excavation and networking.
#pragma warning disable RA0002

/// <summary>
/// Reproduction for clients losing sync with grid chunk fixtures while bulk lasers excavate a planetoid:
/// every chunk fixture id a client knows must exist in that client's fixtures of the same grid.
/// </summary>
[TestFixture]
public sealed class BulkMiningClientSyncTest
{
    private const int Radius = 20;
    private const int EmitterCount = 4;
    private const int EmitterSpacing = 4;
    private const int MaxTicks = 1800;

    private static readonly FieldInfo ChunksField =
        typeof(MapGridComponent).GetField("Chunks", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo ChunkFixturesField =
        typeof(MapGridComponent).Assembly.GetType("Robust.Shared.Map.MapChunk")!
            .GetField("Fixtures", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Test]
    public async Task ClientGridStaysConsistentWhileLasersExcavate([Values(false, true)] bool pvs)
    {
        Assert.That(ChunksField, Is.Not.Null);
        Assert.That(ChunkFixturesField, Is.Not.Null);

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var (server, client) = pair;
        if (pvs)
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
            await pair.RunTicksSync(5);
        }

        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        Entity<BulkAutoMiningConsoleComponent> console = default;
        Entity<MapGridComponent> planetoid = default;
        var emitters = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var power = em.System<PowerReceiverSystem>();
            for (var x = -4; x <= 1; x++)
            {
                for (var y = -EmitterCount * EmitterSpacing / 2; y <= EmitterCount * EmitterSpacing / 2; y++)
                    maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }

            var uid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(map.Grid, -3.5f, .5f));
            console = (uid, em.GetComponent<BulkAutoMiningConsoleComponent>(uid));
            console.Comp.MaxRange = 128;
            console.Comp.TilesPerTick = 4;
            console.Comp.ProcessInterval = TimeSpan.FromSeconds(0.1);
            power.SetNeedsPower(uid, false);
            for (var i = 0; i < EmitterCount; i++)
            {
                // Each laser has a 2.8-tile-wide fixture. Overlapping lasers block each other's outgoing rays.
                var y = .5f + (i - (EmitterCount - 1) / 2f) * EmitterSpacing;
                uid = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, .5f, y));
                var emitter = em.GetComponent<BulkAutoMiningEmitterComponent>(uid);
                emitter.SlurryPerTile = new MinMax(1, 1);
                emitter.MaxWarmupYieldBonus = 0;
                emitters.Add(uid);
                power.SetNeedsPower(uid, false);
            }

            // A multi-chunk planetoid crossing chunk borders on both axes, covered with rock like a real one.
            planetoid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            planetoid.Comp.CanSplit = false;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -Radius; x <= Radius; x++)
            {
                for (var y = -Radius; y <= Radius; y++)
                {
                    if (x * x + y * y <= Radius * Radius)
                        tiles.Add((new Vector2i(x, y), map.Tile.Tile));
                }
            }

            maps.SetTiles(planetoid, planetoid.Comp, tiles);
            planetoid.Comp.CanSplit = true;
            em.System<SharedTransformSystem>().SetWorldPosition(planetoid, new Vector2(Radius + 10, 0));
            foreach (var (tile, _) in tiles)
                em.SpawnEntity("AsteroidRock", new EntityCoordinates(planetoid, tile.X + .5f, tile.Y + .5f));

            em.AddComponent<BulkMiningDepositComponent>(planetoid);
            var generated = new BulkMiningDepositGeneratedEvent();
            em.EventBus.RaiseLocalEvent(planetoid, ref generated);

            // The watching player hovers between the ship and the planetoid, as nearby players do.
            var observer = em.SpawnEntity("MobObserver", new EntityCoordinates(map.MapUid, 8, 0));
            server.PlayerMan.SetAttachedEntity(pair.Player!, observer);
        });

        var consoleComp = console.Comp ?? throw new InvalidOperationException("The mining console was not initialized.");
        var planetoidComp = planetoid.Comp ?? throw new InvalidOperationException("The planetoid grid was not initialized.");

        await PoolManager.WaitUntil(server, () =>
            em.GetComponent<ApcPowerReceiverComponent>(console).Powered &&
            emitters.All(e => em.GetComponent<ApcPowerReceiverComponent>(e).Powered));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var mining = em.System<BulkAutoMiningSystem>();
            Assert.That(mining.TrySelectGrid(console, planetoid), Is.True);
            Assert.That(mining.TryStartMining(console), Is.True);
        });

        var cMapId = map.MapId;
        var active = true;
        var startTiles = 0;
        await server.WaitPost(() => startTiles = em.System<SharedMapSystem>().GetAllTiles(planetoid, planetoidComp).Count());
        var tick = 0;
        for (; tick < MaxTicks && active; tick++)
        {
            await pair.RunTicksSync(1);
            string? failure = null;
            await client.WaitPost(() => failure = CheckClient(client.EntMan, client.ResolveDependency<IMapManager>(), cMapId));
            if (failure != null)
            {
                var processed = 0;
                await server.WaitPost(() => processed = consoleComp.ProcessedTiles);
                Assert.Fail($"Client grid desync after {tick} ticks, {processed}/{startTiles} tiles mined (pvs: {pvs}):\n{failure}");
            }

            await server.WaitPost(() => active = consoleComp.Active);
        }

        await pair.RunTicksSync(30);
        var processedTotal = 0;
        await server.WaitPost(() => processedTotal = consoleComp.ProcessedTiles);
        TestContext.Out.WriteLine($"pvs: {pvs}, ticks: {tick}, mined: {processedTotal}/{startTiles}, active: {active}");
        Assert.That(processedTotal, Is.GreaterThan(startTiles / 2), "The lasers must excavate most of the planetoid to exercise sync.");

        string? finalFailure = null;
        await client.WaitPost(() => finalFailure = CheckClient(client.EntMan, client.ResolveDependency<IMapManager>(), cMapId));
        Assert.That(finalFailure, Is.Null);

        var serverTiles = new Dictionary<NetEntity, HashSet<Vector2i>>();
        await server.WaitPost(() => CollectTiles(em, cMapId, serverTiles));
        var clientTiles = new Dictionary<NetEntity, HashSet<Vector2i>>();
        await client.WaitPost(() => CollectTiles(client.EntMan, cMapId, clientTiles));
        Assert.That(clientTiles.Keys, Is.EquivalentTo(serverTiles.Keys), "Client and server must know the same grids.");
        foreach (var (grid, tiles) in serverTiles)
            Assert.That(clientTiles[grid].SetEquals(tiles), Is.True, $"Client tiles of grid {grid} differ from the server.");

        await pair.CleanReturnAsync();
    }

    /// <summary>Returns null when every client grid is consistent, otherwise a description of the broken chunks.</summary>
    private static string? CheckClient(IEntityManager entMan, IMapManager mapManager, MapId mapId)
    {
        var problems = new List<string>();
        var query = entMan.AllEntityQueryEnumerator<MapGridComponent, FixturesComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var fixtures, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            foreach (DictionaryEntry entry in (IDictionary) ChunksField.GetValue(grid)!)
            {
                foreach (var id in (IEnumerable<string>) ChunkFixturesField.GetValue(entry.Value)!)
                {
                    if (!fixtures.Fixtures.ContainsKey(id))
                        problems.Add($"{entMan.ToPrettyString(uid)} chunk {entry.Key}: missing fixture {id}");
                }
            }
        }

        // The exact query is what overlays and grid rendering run every frame.
        var grids = new List<Entity<MapGridComponent>>();
        try
        {
            mapManager.FindGridsIntersecting(mapId, new Box2(-200, -200, 200, 200), ref grids);
        }
        catch (Exception e)
        {
            problems.Add($"Exact grid query threw: {e.GetType().Name}: {e.Message}");
        }

        return problems.Count == 0 ? null : string.Join('\n', problems);
    }

    private static void CollectTiles(IEntityManager entMan, MapId mapId, Dictionary<NetEntity, HashSet<Vector2i>> result)
    {
        var maps = entMan.System<SharedMapSystem>();
        var query = entMan.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != mapId || entMan.HasComponent<MapComponent>(uid))
                continue;

            var tiles = new HashSet<Vector2i>();
            foreach (var tile in maps.GetAllTiles(uid, grid))
                tiles.Add(tile.GridIndices);

            result[entMan.GetNetEntity(uid)] = tiles;
        }
    }
}

#pragma warning restore RA0002
