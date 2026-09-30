using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Content.Client._Exodus.Shuttles.Radar;
using Content.Shared._Exodus.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

// Inspect the client-only radar cache directly to verify incremental outline rebuilds.
#pragma warning disable RA0002

[TestFixture]
public sealed class RadarGridGeometryTest
{
    [Test]
    public async Task ChunkedOutlineMatchesTilesAfterExcavation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var points = new HashSet<Vector2i>();
        EntityUid gridUid = default;
        float previousInterval = 0;

        await client.WaitPost(() =>
        {
            var config = client.ResolveDependency<IConfigurationManager>();
            previousInterval = config.GetCVar(EXCVars.RadarGridRebuildInterval);
            config.SetCVar(EXCVars.RadarGridRebuildInterval, 0f);
        });

        await server.WaitAssertion(() =>
        {
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            gridUid = grid.Owner;
            grid.Comp.CanSplit = false;
            server.EntMan.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(40, 0));
            var changes = new List<(Vector2i, Tile)>();
            // A block crossing negative and positive chunk borders, with an inner cave touching a chunk edge.
            for (var x = -20; x < 21; x++)
            {
                for (var y = -5; y < 18; y++)
                {
                    var point = new Vector2i(x, y);
                    if (x is >= 10 and <= 17 && y is >= 3 and <= 6)
                        continue;

                    points.Add(point);
                    changes.Add((point, map.Tile.Tile));
                }
            }

            server.EntMan.System<SharedMapSystem>().SetTiles(grid, grid.Comp, changes);
        });
        await pair.RunTicksSync(10);

        var version = 0;
        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(gridUid);
            var grid = client.EntMan.GetComponent<MapGridComponent>(uid);
            var geometry = client.EntMan.System<RadarGridGeometrySystem>().GetGeometry((uid, grid));
            AssertMatches(geometry, points);
            version = geometry.Version;
        });

        // Excavate across a chunk border, including a tile whose neighbour lives in the adjacent chunk.
        var removed = new[] { new Vector2i(15, 0), new Vector2i(16, 0), new Vector2i(-1, 17), new Vector2i(-20, -5) };
        await server.WaitAssertion(() =>
        {
            var maps = server.EntMan.System<SharedMapSystem>();
            var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
            foreach (var point in removed)
            {
                maps.SetTile(gridUid, grid, point, Tile.Empty);
                points.Remove(point);
            }
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(gridUid);
            var grid = client.EntMan.GetComponent<MapGridComponent>(uid);
            var geometry = client.EntMan.System<RadarGridGeometrySystem>().GetGeometry((uid, grid));
            Assert.That(geometry.Version, Is.EqualTo(version + 1), "All changes must be applied in one incremental rebuild.");
            Assert.That(geometry.FullRebuild, Is.False);
            AssertMatches(geometry, points);
        });

        await client.WaitPost(() =>
            client.ResolveDependency<IConfigurationManager>().SetCVar(EXCVars.RadarGridRebuildInterval, previousInterval));
        await server.WaitPost(() => server.EntMan.DeleteEntity(gridUid));
        await pair.CleanReturnAsync();
    }

    /// <summary>Benchmark for the chunked cache on a large, cave-riddled asteroid. Run manually.</summary>
    [Test, Explicit]
    public async Task MeasureLargeAsteroidRebuilds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid gridUid = default;
        const int radius = 90;
        await server.WaitAssertion(() =>
        {
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            gridUid = grid.Owner;
            grid.Comp.CanSplit = false;
            server.EntMan.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(300, 0));
            var random = new System.Random(220);
            var changes = new List<(Vector2i, Tile)>();
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                {
                    // Excavated pockets make the outline long, like a planetoid after hours of mining.
                    if (x * x + y * y <= radius * radius && random.Next(5) != 0)
                        changes.Add((new Vector2i(x, y), map.Tile.Tile));
                }
            }

            server.EntMan.System<SharedMapSystem>().SetTiles(grid, grid.Comp, changes);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var config = client.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EXCVars.RadarGridRebuildInterval, 0f);
            var system = client.EntMan.System<RadarGridGeometrySystem>();
            var uid = pair.ToClientUid(gridUid);
            var grid = client.EntMan.GetComponent<MapGridComponent>(uid);
            var watch = Stopwatch.StartNew();
            var geometry = system.GetGeometry((uid, grid));
            var full = watch.Elapsed;
            var maps = client.EntMan.System<SharedMapSystem>();
            watch.Restart();
            const int edits = 32;
            for (var i = 0; i < edits; i++)
            {
                maps.SetTile(uid, grid, new Vector2i(i - 16, radius / 2), Tile.Empty);
                system.GetGeometry((uid, grid));
            }

            var incremental = watch.Elapsed / edits;
            watch.Restart();
            for (var i = 0; i < 1000; i++)
            {
                system.GetGeometry((uid, grid));
            }

            var cached = watch.Elapsed / 1000;

            // The replaced per-control rebuild: every tile side, then pairwise collinear merging.
            var tiles = new HashSet<Vector2i>();
            var enumerator = maps.GetAllTilesEnumerator(uid, grid);
            while (enumerator.MoveNext(out var tile))
            {
                if (tile is { } tileRef)
                    tiles.Add(tileRef.GridIndices);
            }

            watch.Restart();
            var legacy = LegacyOutline(tiles);
            var legacyTime = watch.Elapsed;
            TestContext.Out.WriteLine($"Full build: {full.TotalMilliseconds:0.###} ms; incremental tile edit: " +
                $"{incremental.TotalMilliseconds:0.###} ms; cached draw lookup: {cached.TotalMilliseconds * 1000:0.###} us; " +
                $"fill vertices: {geometry.FillCount}; edge vertices: {geometry.EdgeCount}. " +
                $"Legacy outline merge alone: {legacyTime.TotalMilliseconds:0.###} ms per rebuild; legacy fill vertices: " +
                $"{tiles.Count * 6}; legacy edge vertices: {legacy * 2}.");
            config.SetCVar(EXCVars.RadarGridRebuildInterval, 0.25f);
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(gridUid));
        await pair.CleanReturnAsync();
    }

    /// <summary>The previous outline algorithm for square tiles, kept only to measure it. Returns the segment count.</summary>
    private static int LegacyOutline(HashSet<Vector2i> tiles)
    {
        var edges = new List<(Vector2 Start, Vector2 End)>();
        var corners = new[] { Vector2.Zero, new Vector2(0, 1), Vector2.One, new Vector2(1, 0) };
        var sides = new[] { new Vector2i(0, -1), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(1, 0) };
        foreach (var tile in tiles)
        {
            var bottomLeft = new Vector2(tile.X, tile.Y);
            for (var i = 0; i < 4; i++)
            {
                if (!tiles.Contains(tile + sides[i]))
                    edges.Add((bottomLeft + corners[(i + 3) % 4], bottomLeft + corners[i]));
            }
        }

        var decomposed = true;
        while (decomposed)
        {
            decomposed = false;
            for (var i = 0; i < edges.Count; i++)
            {
                var (start, end) = edges[i];
                var neighborIndex = -1;
                var neighborEnd = Vector2.Zero;
                for (var j = i + 1; j < edges.Count; j++)
                {
                    if (!end.Equals(edges[j].Start))
                        continue;

                    neighborIndex = j;
                    neighborEnd = edges[j].End;
                    break;
                }

                if (neighborIndex < 0 || !Robust.Shared.Physics.CollinearSimplifier.IsCollinear(start, end, neighborEnd, 10f * float.Epsilon))
                    continue;

                decomposed = true;
                edges[i] = (start, neighborEnd);
                edges.RemoveAt(neighborIndex);
            }
        }

        return edges.Count;
    }

    private static void AssertMatches(RadarGridGeometryComponent geometry, HashSet<Vector2i> points)
    {
        Assert.That(geometry.FillCount % 3, Is.Zero);
        Assert.That(geometry.EdgeCount % 2, Is.Zero);

        // Merged rectangles must cover exactly the tiles, without overlap.
        var area = 0f;
        for (var i = 0; i < geometry.FillCount; i += 3)
        {
            var a = geometry.Fill[i];
            var b = geometry.Fill[i + 1];
            var c = geometry.Fill[i + 2];
            area += MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5f;
        }

        Assert.That(area, Is.EqualTo(points.Count).Within(0.001f));

        // The outline must be exactly the set of tile sides facing empty space.
        var expected = 0;
        var sides = new List<(Vector2 Start, Vector2 End)>();
        foreach (var point in points)
        {
            foreach (var direction in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
            {
                if (points.Contains(point + direction))
                    continue;

                expected++;
                var center = new Vector2(point.X + 0.5f, point.Y + 0.5f);
                var normal = new Vector2(direction.X, direction.Y) * 0.5f;
                var tangent = new Vector2(-normal.Y, normal.X);
                sides.Add((center + normal - tangent, center + normal + tangent));
            }
        }

        var length = 0f;
        for (var i = 0; i < geometry.EdgeCount; i += 2)
        {
            length += Vector2.Distance(geometry.Edges[i], geometry.Edges[i + 1]);
        }

        Assert.That(length, Is.EqualTo(expected).Within(0.001f), "Outline length must equal the exposed tile sides.");
        Assert.That(geometry.EdgeCount / 2, Is.LessThan(expected), "Collinear sides must be merged.");
        foreach (var (start, end) in sides)
        {
            var middle = (start + end) * 0.5f;
            var covered = false;
            for (var i = 0; i < geometry.EdgeCount && !covered; i += 2)
            {
                covered = IsOnSegment(middle, geometry.Edges[i], geometry.Edges[i + 1]);
            }

            Assert.That(covered, Is.True, $"Exposed side {start} - {end} is missing from the outline.");
        }
    }

    private static bool IsOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var cross = segment.X * (point.Y - start.Y) - segment.Y * (point.X - start.X);
        if (MathF.Abs(cross) > 0.0001f)
            return false;

        var dot = Vector2.Dot(point - start, segment);
        return dot >= 0 && dot <= segment.LengthSquared();
    }
}

#pragma warning restore RA0002
