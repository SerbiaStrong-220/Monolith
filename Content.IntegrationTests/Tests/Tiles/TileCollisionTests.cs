using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.Tiles;

[TestFixture]
public sealed class TileCollisionTests
{
    private static readonly string[] PartialTileIds =
    {
        "LatticeCornerNW",
        "PlatingCornerNW",
        "LatticeHalfV",
        "LatticeHalfTiltNESLower",
        "LatticeHalfTiltNESUpper",
        "PlatingHalfV",
        "PlatingHalfTiltNESLower",
        "PlatingHalfTiltNESUpper",
    };

    [TestCaseSource(nameof(PartialTileIds))]
    public async Task PartialTileUsesPolygonGridFixture(string tileId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            var tileDef = tileDefs[tileId];
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDef.TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;
            Assert.That(fixtures.Keys, Has.Some.StartsWith("grid_tile-"), "partial tiles should create per-tile fixtures");
            Assert.That(fixtures.Keys, Has.None.StartsWith("grid_chunk-"), "single partial tile should not create square chunk fixtures");

            var tileFixture = fixtures.First(pair => pair.Key.StartsWith("grid_tile-")).Value;
            Assert.That(tileFixture.Shape, Is.TypeOf<PolygonShape>());

            var shape = (PolygonShape) tileFixture.Shape;
            Assert.That(shape.VertexCount, Is.LessThan(4).Or.EqualTo(4), $"{tileId} should use its own polygon fixture");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PartialTileNextToFullLatticeKeepsSeparatePolygonFixture()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDefs["LatticeCornerNW"].TileId));
            mapSystem.SetTile(grid, new Vector2i(1, 0), new Tile(tileDefs["Lattice"].TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;
            Assert.That(fixtures.Keys, Has.Some.EqualTo("grid_tile-0-0"), "partial tile should keep its own fixture");
            Assert.That(fixtures.Keys, Has.Some.StartsWith("grid_chunk-"), "full lattice should still use merged chunk fixture");

            var partialFixture = fixtures["grid_tile-0-0"];
            Assert.That(partialFixture.Shape, Is.TypeOf<PolygonShape>());

            var partialShape = (PolygonShape) partialFixture.Shape;
            Assert.That(partialShape.VertexCount, Is.EqualTo(3), "partial tile next to full lattice must not become a square");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PartialTileIntersectionUsesPolygonShape()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var mapMan = server.MapMan;
        var mapSystem = server.EntMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDefs["LatticeCornerNW"].TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(QueryGridAt(mapMan, testMap.MapId, new Box2(0.05f, 0.85f, 0.15f, 0.95f)), Contains.Item(grid));
            Assert.That(QueryGridAt(mapMan, testMap.MapId, new Box2(0.85f, 0.05f, 0.95f, 0.15f)), Does.Not.Contain(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PartialTileOnPreviouslyEmptyGridEnablesCollision()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            var physics = entMan.GetComponent<PhysicsComponent>(grid);
            entMan.System<SharedPhysicsSystem>().SetCanCollide(grid, false, body: physics);

            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDefs["LatticeCornerNW"].TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var physics = entMan.GetComponent<PhysicsComponent>(grid);
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;

            Assert.That(physics.CanCollide, Is.True, "partial tile fixtures should re-enable grid collision");
            Assert.That(fixtures.Keys, Has.Some.EqualTo("grid_tile-0-0"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReinforcedHullTileKeepsDefaultGridCollision()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var transformSystem = entMan.System<SharedTransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            transformSystem.SetWorldPosition(grid, new Vector2(100, 100));
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDefs["FloorHullReinforced"].TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;

            Assert.That(tileDefs["FloorHullReinforced"].HasCollision, Is.True);
            Assert.That(fixtures.Keys, Has.Some.StartsWith("grid_chunk-"));
            Assert.That(fixtures.Keys, Has.None.StartsWith("grid_tile-"));
            Assert.That(QueryGridAt(mapMan, testMap.MapId, new Box2(100.25f, 100.25f, 100.75f, 100.75f)), Contains.Item(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoCollisionReinforcedHullStillSupportsGridLookup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var transformSystem = entMan.System<SharedTransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            transformSystem.SetWorldPosition(grid, new Vector2(100, 100));
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(tileDefs["FloorHullReinforcedNoCollision"].TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;

            Assert.That(tileDefs["FloorHullReinforcedNoCollision"].HasCollision, Is.False);
            Assert.That(fixtures.Keys, Has.None.StartsWith("grid_chunk-"));
            Assert.That(fixtures.Keys, Has.None.StartsWith("grid_tile-"));
            Assert.That(QueryGridAt(mapMan, testMap.MapId, new Box2(100.25f, 100.25f, 100.75f, 100.75f)), Contains.Item(grid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllPartialTilesCanBeIntersectedAtTheirCentroid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var mapMan = server.MapMan;
        var mapSystem = server.EntMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
        });

        var partialDefs = tileDefs
            .Where(def => def is { HasCollision: true, CollisionVertices: not null })
            .Where(def => def.ID.Contains("Lattice") || def.ID.Contains("Plating"))
            .ToArray();

        Assert.That(partialDefs, Is.Not.Empty);

        var x = 0;
        await server.WaitAssertion(() =>
        {
            foreach (var tileDef in partialDefs)
            {
                mapSystem.SetTile(grid, new Vector2i(x++, 0), new Tile(tileDef.TileId));
            }
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < partialDefs.Length; i++)
            {
                var tileDef = partialDefs[i];
                var centroid = GetCentroid(tileDef.CollisionVertices!);
                var center = new Vector2(i, 0) + centroid;
                var queryBox = Box2.CenteredAround(center, new Vector2(0.05f, 0.05f));

                Assert.That(QueryGridAt(mapMan, testMap.MapId, queryBox), Contains.Item(grid), $"{tileDef.ID} should collide at its centroid");
            }
        });

        await pair.CleanReturnAsync();
    }

    private static List<Entity<MapGridComponent>> QueryGridAt(IMapManager mapMan, MapId mapId, Box2 bounds)
    {
        var grids = new List<Entity<MapGridComponent>>();
        mapMan.FindGridsIntersecting(mapId, bounds, ref grids, approx: false, includeMap: false);
        return grids;
    }

    private static Vector2 GetCentroid(IReadOnlyList<Vector2> vertices)
    {
        var centroid = Vector2.Zero;

        foreach (var vertex in vertices)
        {
            centroid += vertex;
        }

        return centroid / vertices.Count;
    }

    [Test]
    public async Task InvisibleSupportDoesNotCreateGridFixture()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.MapMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        Entity<MapGridComponent> grid = default;

        await server.WaitAssertion(() =>
        {
            grid = mapMan.CreateGridEntity(testMap.MapId);
            var support = tileDefs["InvisibleSupport"];
            mapSystem.SetTile(grid, Vector2i.Zero, new Tile(support.TileId));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fixtures = entMan.GetComponent<FixturesComponent>(grid).Fixtures;
            Assert.That(fixtures, Is.Empty, "InvisibleSupport should not create grid collision fixtures");
        });

        await pair.CleanReturnAsync();
    }
}
