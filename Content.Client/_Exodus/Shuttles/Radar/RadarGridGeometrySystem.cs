using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.GridEdgeMarker;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Shuttles.Radar;

/// <summary>
/// Builds radar outlines once per grid for all mass scanners. Tile changes rebuild only the affected
/// chunks, fills are merged into rectangles and outlines are merged through a hash of segment starts.
/// </summary>
public sealed partial class RadarGridGeometrySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Side of a cached geometry chunk, in tiles. Independent of the engine's grid chunks.</summary>
    public const int ChunkSize = 16;

    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UnusedLifetime = TimeSpan.FromSeconds(60);

    /// <summary>West, east, south, north; the opposite side of index <c>i</c> is <c>i ^ 1</c>.</summary>
    private static readonly Vector2i[] SideOffsets =
    [
        new(-1, 0),
        new(1, 0),
        new(0, -1),
        new(0, 1),
    ];

    private EntityQuery<RadarGridGeometryComponent> _geometryQuery;
    private EntityQuery<GridEdgeMarkerComponent> _markerQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private TimeSpan _rebuildInterval;
    private TimeSpan _nextPrune;

    private RadarTileShape?[] _shapes = Array.Empty<RadarTileShape?>();

    // Scratch buffers of a single chunk build. Rendering runs on the main thread only.
    private Tile[] _window = Array.Empty<Tile>();
    private bool[] _squares = Array.Empty<bool>();
    private readonly List<(Vector2 Start, Vector2 End)> _edges = new();
    private readonly List<int> _nextWithStart = new();
    private readonly List<bool> _alive = new();
    private readonly Dictionary<Vector2, int> _starts = new();
    private readonly List<EntityUid> _pruneScratch = new();

    public override void Initialize()
    {
        base.Initialize();
        _geometryQuery = GetEntityQuery<RadarGridGeometryComponent>();
        _markerQuery = GetEntityQuery<GridEdgeMarkerComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<RadarGridGeometryComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<GridEdgeMarkerComponent, ComponentStartup>(OnMarkerStartup);
        SubscribeLocalEvent<GridEdgeMarkerComponent, ComponentShutdown>(OnMarkerShutdown);
        SubscribeLocalEvent<GridEdgeMarkerComponent, MoveEvent>(OnMarkerMoved);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        Subs.CVar(_cfg, EXCVars.RadarGridRebuildInterval,
            value => _rebuildInterval = TimeSpan.FromSeconds(float.IsFinite(value) ? Math.Max(0f, value) : 0f), true);
    }

    /// <summary>
    /// Returns the grid's radar geometry, rebuilding dirty chunks when the rebuild interval allows it.
    /// A heavily modified grid keeps drawing its previous outline between rebuilds.
    /// </summary>
    public RadarGridGeometryComponent GetGeometry(Entity<MapGridComponent> grid)
    {
        var geometry = EnsureComp<RadarGridGeometryComponent>(grid);
        var now = _timing.RealTime;
        geometry.LastUsed = now;
        UpdateGeometry(grid, geometry, now);
        return geometry;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.RealTime;
        if (now < _nextPrune)
            return;

        _nextPrune = now + PruneInterval;
        _pruneScratch.Clear();
        var query = AllEntityQuery<RadarGridGeometryComponent>();
        while (query.MoveNext(out var uid, out var geometry))
        {
            if (now - geometry.LastUsed > UnusedLifetime)
                _pruneScratch.Add(uid);
        }

        foreach (var uid in _pruneScratch)
        {
            RemComp<RadarGridGeometryComponent>(uid);
        }
    }

    private void OnTileChanged(Entity<RadarGridGeometryComponent> ent, ref TileChangedEvent args)
    {
        const int size = ChunkSize;
        foreach (var change in args.Changes)
        {
            var chunk = SharedMapSystem.GetChunkIndices(change.GridIndices, size);
            ent.Comp.DirtyChunks.Add(chunk);

            // Outlines depend on the four neighbours, which may live in the adjacent chunk.
            var local = change.GridIndices - chunk * size;
            if (local.X == 0)
                ent.Comp.DirtyChunks.Add(chunk + SideOffsets[0]);
            else if (local.X == size - 1)
                ent.Comp.DirtyChunks.Add(chunk + SideOffsets[1]);

            if (local.Y == 0)
                ent.Comp.DirtyChunks.Add(chunk + SideOffsets[2]);
            else if (local.Y == size - 1)
                ent.Comp.DirtyChunks.Add(chunk + SideOffsets[3]);
        }

        ent.Comp.TileEventsSeen = true;
    }

    private void OnMarkerStartup(Entity<GridEdgeMarkerComponent> ent, ref ComponentStartup args)
    {
        AddMarker(ent, Transform(ent).ParentUid);
    }

    private void OnMarkerShutdown(Entity<GridEdgeMarkerComponent> ent, ref ComponentShutdown args)
    {
        if (_xformQuery.TryComp(ent, out var xform))
            RemoveMarker(ent, xform.ParentUid);
    }

    private void OnMarkerMoved(Entity<GridEdgeMarkerComponent> ent, ref MoveEvent args)
    {
        var oldParent = args.OldPosition.EntityId;
        var newParent = args.NewPosition.EntityId;
        if (oldParent != newParent)
            RemoveMarker(ent, oldParent);

        AddMarker(ent, newParent);
    }

    private void AddMarker(EntityUid marker, EntityUid parent)
    {
        if (!_geometryQuery.TryComp(parent, out var geometry))
            return;

        geometry.MarkersDirty = true;
        if (geometry.MarkersCollected)
            geometry.Markers.Add(marker);
    }

    private void RemoveMarker(EntityUid marker, EntityUid parent)
    {
        if (!_geometryQuery.TryComp(parent, out var geometry))
            return;

        geometry.Markers.Remove(marker);
        geometry.MarkersDirty = true;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ContentTileDefinition>())
            return;

        Array.Clear(_shapes);
        var query = AllEntityQuery<RadarGridGeometryComponent>();
        while (query.MoveNext(out var geometry))
        {
            geometry.FullRebuild = true;
        }
    }

    private void UpdateGeometry(Entity<MapGridComponent> grid, RadarGridGeometryComponent geometry, TimeSpan now)
    {
        // Changes that did not raise tile events (or were suppressed) cannot be patched chunk by chunk.
        var tick = grid.Comp.LastTileModifiedTick;
        if (tick != geometry.LastTileModifiedTick)
        {
            if (!geometry.TileEventsSeen)
                geometry.FullRebuild = true;

            geometry.LastTileModifiedTick = tick;
            geometry.TileEventsSeen = false;
        }

        if (!geometry.FullRebuild && geometry.DirtyChunks.Count == 0 && !geometry.MarkersDirty)
            return;

        // Continuous excavation must not rebuild on every tick; the first outline is always built at once.
        if (geometry.Version > 0 && now < geometry.NextRebuild)
            return;

        geometry.NextRebuild = now + _rebuildInterval;
        if (geometry.FullRebuild)
        {
            geometry.FullRebuild = false;
            CollectChunks(grid, geometry);
        }

        foreach (var index in geometry.DirtyChunks)
        {
            if (!geometry.Chunks.TryGetValue(index, out var chunk))
            {
                chunk = new RadarGridChunkGeometry();
                geometry.Chunks.Add(index, chunk);
            }

            BuildChunk(grid, index, chunk);
            if (chunk.Fill.Count == 0 && chunk.Edges.Count == 0)
                geometry.Chunks.Remove(index);
        }

        geometry.DirtyChunks.Clear();
        if (geometry.MarkersDirty)
            RebuildMarkers(grid, geometry);

        Assemble(geometry);
        geometry.Version++;
    }

    private void CollectChunks(Entity<MapGridComponent> grid, RadarGridGeometryComponent geometry)
    {
        // Existing chunks are rebuilt too, so chunks emptied by unexplained changes are released.
        geometry.DirtyChunks.Clear();
        foreach (var index in geometry.Chunks.Keys)
        {
            geometry.DirtyChunks.Add(index);
        }

        var tiles = _map.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is { } tileRef)
                geometry.DirtyChunks.Add(SharedMapSystem.GetChunkIndices(tileRef.GridIndices, ChunkSize));
        }
    }

    private void BuildChunk(Entity<MapGridComponent> grid, Vector2i chunkIndex, RadarGridChunkGeometry chunk)
    {
        chunk.Fill.Clear();
        chunk.Edges.Clear();

        const int size = ChunkSize;
        const int span = size + 2;
        EnsureCapacity(ref _window, span * span);
        EnsureCapacity(ref _squares, size * size);
        var origin = chunkIndex * size;

        // Copy the chunk and its one-tile border once; every neighbour lookup below is then an array read.
        var any = false;
        for (var y = -1; y <= size; y++)
        {
            for (var x = -1; x <= size; x++)
            {
                var inside = x >= 0 && y >= 0 && x < size && y < size;
                var tile = Tile.Empty;
                if ((inside || x >= 0 && x < size || y >= 0 && y < size) &&
                    _map.TryGetTile(grid.Comp, new Vector2i(origin.X + x, origin.Y + y), out var value))
                {
                    tile = value;
                }

                _window[(y + 1) * span + x + 1] = tile;
                any |= inside && !tile.IsEmpty;
            }
        }

        if (!any)
            return;

        var tileSize = (float)grid.Comp.TileSize;
        _edges.Clear();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var tile = _window[(y + 1) * span + x + 1];
                if (tile.IsEmpty)
                    continue;

                var shape = GetShape(tile.TypeId);
                var bottomLeft = new Vector2((origin.X + x) * tileSize, (origin.Y + y) * tileSize);
                if (shape.FullSquare)
                    _squares[y * size + x] = true;
                else
                    AddTriangleFan(chunk.Fill, shape, bottomLeft, tileSize);

                AddTileEdges(shape, bottomLeft, tileSize, x, y, span);
            }
        }

        AddSquareFill(chunk.Fill, origin, size, tileSize);
        MergeEdges(chunk.Edges);
    }

    private static void AddTriangleFan(List<Vector2> fill, RadarTileShape shape, Vector2 bottomLeft, float tileSize)
    {
        // Tile shapes are convex, so a fan around the first vertex covers them.
        var vertices = shape.Vertices;
        var first = bottomLeft + vertices[0] * tileSize;
        var previous = bottomLeft + vertices[1] * tileSize;
        for (var i = 2; i < vertices.Length; i++)
        {
            var vertex = bottomLeft + vertices[i] * tileSize;
            fill.Add(first);
            fill.Add(previous);
            fill.Add(vertex);
            previous = vertex;
        }
    }

    /// <summary>Merges full square tiles into maximal rectangles; asteroids need a fraction of the per-tile triangles.</summary>
    private void AddSquareFill(List<Vector2> fill, Vector2i origin, int size, float tileSize)
    {
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!_squares[y * size + x])
                    continue;

                var width = 1;
                while (x + width < size && _squares[y * size + x + width])
                {
                    width++;
                }

                var height = 1;
                while (y + height < size && IsSquareRow(y + height, x, width, size))
                {
                    height++;
                }

                for (var row = y; row < y + height; row++)
                {
                    Array.Clear(_squares, row * size + x, width);
                }

                var bottomLeft = new Vector2((origin.X + x) * tileSize, (origin.Y + y) * tileSize);
                var topRight = bottomLeft + new Vector2(width * tileSize, height * tileSize);
                var topLeft = new Vector2(bottomLeft.X, topRight.Y);
                var bottomRight = new Vector2(topRight.X, bottomLeft.Y);
                fill.Add(bottomLeft);
                fill.Add(topLeft);
                fill.Add(topRight);
                fill.Add(bottomLeft);
                fill.Add(topRight);
                fill.Add(bottomRight);
            }
        }
    }

    private bool IsSquareRow(int row, int x, int width, int size)
    {
        var start = row * size + x;
        for (var i = 0; i < width; i++)
        {
            if (!_squares[start + i])
                return false;
        }

        return true;
    }

    /// <summary>Adds the parts of this tile's outline not covered by a neighbouring tile's side.</summary>
    private void AddTileEdges(RadarTileShape shape, Vector2 bottomLeft, float tileSize, int x, int y, int span)
    {
        var vertices = shape.Vertices;
        var previous = vertices[^1];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            var wasPrevious = previous;
            previous = vertex;

            var side = shape.EdgeSides[i];
            if (side >= 0)
            {
                var offset = SideOffsets[side];
                var neighbor = _window[(y + 1 + offset.Y) * span + x + 1 + offset.X];
                if (!neighbor.IsEmpty)
                {
                    var neighborShape = GetShape(neighbor.TypeId);
                    if (shape.FullSquare && neighborShape.FullSquare)
                        continue;

                    if (neighborShape.Sides[side ^ 1] is { } other &&
                        TryClipEdge(other, offset, wasPrevious, vertex, bottomLeft, tileSize))
                    {
                        continue;
                    }
                }
            }

            _edges.Add((bottomLeft + wasPrevious * tileSize, bottomLeft + vertex * tileSize));
        }
    }

    /// <summary>
    /// Mono overlap rules: hides the part of an edge lying on the neighbour's opposite side.
    /// Returns false when the edges do not overlap and the whole edge must be drawn.
    /// </summary>
    private bool TryClipEdge(
        (Vector2 Start, Vector2 End) other,
        Vector2i side,
        Vector2 wasPrevious,
        Vector2 vertex,
        Vector2 bottomLeft,
        float tileSize)
    {
        var offset = (Vector2)side;
        var otherPrevious = other.Start + offset;
        var otherVertex = other.End + offset;
        var otherEdge = otherVertex - otherPrevious;
        var otherAdjusted = otherEdge / otherEdge.LengthSquared();
        var previousPosition = Vector2.Dot(wasPrevious - otherPrevious, otherAdjusted);
        var vertexPosition = Vector2.Dot(vertex - otherPrevious, otherAdjusted);
        if (previousPosition > vertexPosition)
            (vertexPosition, previousPosition) = (previousPosition, vertexPosition);

        // Fully inside the other edge: nothing to draw.
        if (previousPosition >= 0 && vertexPosition <= 1)
            return true;

        // No overlap at all.
        if (previousPosition >= 1 || vertexPosition <= 0)
            return false;

        if (previousPosition >= 0 || vertexPosition <= 1)
        {
            if (vertexPosition <= 1)
                vertexPosition = 0;

            if (previousPosition >= 0)
                previousPosition = 1;

            var first = otherPrevious + otherEdge * previousPosition;
            var second = otherPrevious + otherEdge * vertexPosition;
            if (second - first != Vector2.Zero)
                _edges.Add((bottomLeft + first * tileSize, bottomLeft + second * tileSize));

            return true;
        }

        // This edge encompasses the other one: draw both uncovered parts.
        var start = otherPrevious + otherEdge * previousPosition;
        var end = otherPrevious + otherEdge * vertexPosition;
        _edges.Add((bottomLeft + start * tileSize, bottomLeft + otherPrevious * tileSize));
        _edges.Add((bottomLeft + otherVertex * tileSize, bottomLeft + end * tileSize));
        return true;
    }

    /// <summary>Joins consecutive collinear segments using a hash of segment starts instead of pairwise scans.</summary>
    private void MergeEdges(List<Vector2> output)
    {
        var count = _edges.Count;
        _starts.Clear();
        _nextWithStart.Clear();
        _alive.Clear();
        for (var i = 0; i < count; i++)
        {
            // The content sandbox forbids CollectionsMarshal.GetValueRefOrAddDefault, so update the chain head explicitly.
            var edgeStart = _edges[i].Start;
            _nextWithStart.Add(_starts.TryGetValue(edgeStart, out var head) ? head : -1);
            _alive.Add(true);
            _starts[edgeStart] = i;
        }

        for (var i = 0; i < count; i++)
        {
            if (!_alive[i])
                continue;

            var (start, end) = _edges[i];
            // Each merge consumes a live segment, which bounds the chain walk.
            for (var guard = 0; guard < count; guard++)
            {
                if (!_starts.TryGetValue(end, out var candidate))
                    break;

                var found = -1;
                for (; candidate >= 0; candidate = _nextWithStart[candidate])
                {
                    if (candidate == i || !_alive[candidate])
                        continue;

                    var next = _edges[candidate].End;
                    if (Vector2.Dot(end - start, next - end) <= 0f ||
                        !CollinearSimplifier.IsCollinear(start, end, next, 10f * float.Epsilon))
                    {
                        continue;
                    }

                    found = candidate;
                    break;
                }

                if (found < 0)
                    break;

                end = _edges[found].End;
                _alive[found] = false;
            }

            _edges[i] = (start, end);
        }

        for (var i = 0; i < count; i++)
        {
            if (!_alive[i])
                continue;

            var (start, end) = _edges[i];
            output.Add(start);
            output.Add(end);
        }
    }

    private void RebuildMarkers(Entity<MapGridComponent> grid, RadarGridGeometryComponent geometry)
    {
        geometry.MarkersDirty = false;
        if (!geometry.MarkersCollected)
        {
            // Markers are rare map fixtures; one component scan replaces a lookup over every entity of the grid.
            geometry.MarkersCollected = true;
            geometry.Markers.Clear();
            var query = AllEntityQuery<GridEdgeMarkerComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.ParentUid == grid.Owner)
                    geometry.Markers.Add(uid);
            }
        }

        geometry.MarkerEdges.Clear();
        var tileSize = (float)grid.Comp.TileSize;
        foreach (var uid in geometry.Markers)
        {
            if (!_markerQuery.TryComp(uid, out var marker) || !_xformQuery.TryComp(uid, out var xform) ||
                xform.ParentUid != grid.Owner)
            {
                continue;
            }

            var position = xform.LocalPosition;
            var rotation = xform.LocalRotation;
            geometry.MarkerEdges.Add(position + rotation.RotateVec(marker.Begin) * tileSize);
            geometry.MarkerEdges.Add(position + rotation.RotateVec(marker.End) * tileSize);
        }
    }

    private static void Assemble(RadarGridGeometryComponent geometry)
    {
        var fillCount = 0;
        var edgeCount = geometry.MarkerEdges.Count;
        foreach (var chunk in geometry.Chunks.Values)
        {
            fillCount += chunk.Fill.Count;
            edgeCount += chunk.Edges.Count;
        }

        EnsureCapacity(ref geometry.Fill, fillCount);
        EnsureCapacity(ref geometry.Edges, edgeCount);
        var fillOffset = 0;
        var edgeOffset = 0;
        foreach (var chunk in geometry.Chunks.Values)
        {
            CollectionsMarshal.AsSpan(chunk.Fill).CopyTo(geometry.Fill.AsSpan(fillOffset));
            fillOffset += chunk.Fill.Count;
            CollectionsMarshal.AsSpan(chunk.Edges).CopyTo(geometry.Edges.AsSpan(edgeOffset));
            edgeOffset += chunk.Edges.Count;
        }

        CollectionsMarshal.AsSpan(geometry.MarkerEdges).CopyTo(geometry.Edges.AsSpan(edgeOffset));
        geometry.FillCount = fillCount;
        geometry.EdgeCount = edgeCount;
    }

    private RadarTileShape GetShape(int typeId)
    {
        if (typeId < _shapes.Length && _shapes[typeId] is { } cached)
            return cached;

        if (typeId >= _shapes.Length)
            Array.Resize(ref _shapes, Math.Max(typeId + 1, Math.Max(_shapes.Length * 2, _tileDefinitions.Count)));

        var vertices = _tileDefinitions[typeId] is ContentTileDefinition { Vertices.Count: >= 3 } definition
            ? definition.Vertices.ToArray()
            : RadarTileShape.SquareVertices;
        var shape = new RadarTileShape(vertices);
        _shapes[typeId] = shape;
        return shape;
    }

    private static void EnsureCapacity<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            Array.Resize(ref array, Math.Max(length, array.Length * 2));
    }

    private sealed class RadarTileShape
    {
        public static readonly Vector2[] SquareVertices =
        [
            Vector2.Zero,
            new(0, 1),
            new(1, 1),
            new(1, 0),
        ];

        /// <summary>Convex outline in tile-local units.</summary>
        public readonly Vector2[] Vertices;

        /// <summary>Tile side of the edge ending at each vertex, or -1 when the edge is inside the tile.</summary>
        public readonly int[] EdgeSides;

        /// <summary>The outline edge lying on each tile side, if any.</summary>
        public readonly (Vector2 Start, Vector2 End)?[] Sides = new (Vector2 Start, Vector2 End)?[4];

        public readonly bool FullSquare;

        public RadarTileShape(Vector2[] vertices)
        {
            Vertices = vertices;
            EdgeSides = new int[vertices.Length];
            var previous = vertices[^1];
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                var side = previous.X == 0 && vertex.X == 0 ? 0
                    : previous.X == 1 && vertex.X == 1 ? 1
                    : previous.Y == 0 && vertex.Y == 0 ? 2
                    : previous.Y == 1 && vertex.Y == 1 ? 3
                    : -1;

                EdgeSides[i] = side;
                if (side >= 0)
                    Sides[side] = (previous, vertex);

                previous = vertex;
            }

            if (vertices.Length != 4)
                return;

            var corners = 0;
            foreach (var vertex in vertices)
            {
                if (vertex == Vector2.Zero)
                    corners |= 1;
                else if (vertex == new Vector2(0, 1))
                    corners |= 2;
                else if (vertex == Vector2.One)
                    corners |= 4;
                else if (vertex == new Vector2(1, 0))
                    corners |= 8;
            }

            FullSquare = corners == 15;
        }
    }
}
