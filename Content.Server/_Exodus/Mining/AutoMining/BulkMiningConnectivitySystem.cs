using System.Diagnostics;
using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>Allows excavation only when the remaining four-neighbor tile graph stays connected.</summary>
public sealed partial class BulkMiningConnectivitySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    private const int MaxStepsPerUpdate = 16384;
    private const int StepsPerGrid = 128;
    private const int NodePageBits = 8;
    private const int NodePageSize = 1 << NodePageBits;
    private static readonly TimeSpan UpdateBudget = TimeSpan.FromMilliseconds(0.5);

    private readonly List<Entity<BulkMiningConnectivityComponent, MapGridComponent>> _pending = new();
    private EntityQuery<BulkMiningConnectivityComponent> _cacheQuery;
    private int _nextGridIndex;

    public override void Initialize()
    {
        base.Initialize();
        _cacheQuery = GetEntityQuery<BulkMiningConnectivityComponent>();
        UpdatesBefore.Add(typeof(BulkAutoMiningSystem));
        SubscribeLocalEvent<BulkMiningConnectivityComponent, TileChangedEvent>(OnTileChanged);
    }

    public void RetainGrid(EntityUid user, EntityUid grid)
    {
        if (!TerminatingOrDeleted(grid))
            EnsureComp<BulkMiningConnectivityComponent>(grid).Users.Add(user);
    }

    public void ReleaseGrid(EntityUid user, EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<BulkMiningConnectivityComponent>(grid, out var comp))
            return;

        comp.Users.Remove(user);
        if (comp.Users.Count == 0)
            RemCompDeferred<BulkMiningConnectivityComponent>(grid);
    }

    /// <summary>
    /// Checks the actual beam hit, not its originally requested target. Pending never authorizes removal.
    /// Retain the grid for the lifetime of the caller's mining job to share and release expensive analyses.
    /// </summary>
    public BulkMiningTileSafety GetTileSafety(Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (TerminatingOrDeleted(grid) || EntityManager.IsQueuedForDeletion(grid))
            return BulkMiningTileSafety.Unsafe;

        var neighbors = -1;
        if (_cacheQuery.TryComp(grid, out var comp) && comp.NodeIndices.TryGetValue(tile, out var index))
        {
            ref var node = ref GetNode(comp, index);
            if (comp.Complete && node.Generation == comp.Generation)
                return node.Parts > 1 ? BulkMiningTileSafety.Unsafe : BulkMiningTileSafety.Safe;

            if (node.NeighborsKnown)
                neighbors = node.Neighbors;
        }

        if (neighbors < 0)
        {
            if (!HasTile(grid.Comp, tile))
                return BulkMiningTileSafety.Unsafe;

            neighbors = GetNeighbors(grid.Comp, tile);
        }

        // Most surface tiles are provably safe using just their 3x3 neighborhood.
        // All nonempty floor tiles count, including constructed tiles without a natural deposit.
        if (IsLocallySafe(grid.Comp, tile, neighbors))
            return BulkMiningTileSafety.Safe;

        comp ??= EnsureComp<BulkMiningConnectivityComponent>(grid);
        if (!comp.Pending)
            StartAnalysis((grid, comp, grid.Comp), tile);

        return BulkMiningTileSafety.Pending;
    }

    private bool HasTile(MapGridComponent grid, Vector2i tile)
    {
        return _map.TryGetTile(grid, tile, out var value) && !value.IsEmpty;
    }

    private byte GetNeighbors(MapGridComponent grid, Vector2i tile)
    {
        byte neighbors = 0;
        for (var direction = 0; direction < 4; direction++)
        {
            if (HasTile(grid, Neighbor(tile, direction)))
                neighbors |= (byte)(1 << direction);
        }

        return neighbors;
    }

    private bool IsLocallySafe(MapGridComponent grid, Vector2i tile, int cardinal)
    {
        if ((cardinal & (cardinal - 1)) == 0)
            return true;

        // Clockwise ring: E, NE, N, NW, W, SW, S, SE. Edges only join cardinal tile neighbors.
        var occupied = 0;
        for (var direction = 0; direction < 4; direction++)
        {
            if ((cardinal & (1 << direction)) != 0)
                occupied |= 1 << (direction * 2);
        }

        var neighbors = occupied;
        for (var direction = 0; direction < 4; direction++)
        {
            var corner = Neighbor(Neighbor(tile, direction), (direction + 1) % 4);
            if (HasTile(grid, corner))
                occupied |= 1 << (direction * 2 + 1);
        }

        var reached = neighbors & -neighbors;
        while (true)
        {
            var adjacent = (reached << 1) | (reached >> 7) | (reached >> 1) | (reached << 7);
            var expanded = reached | (adjacent & occupied);
            if (expanded == reached)
                return (reached & neighbors) == neighbors;

            reached = expanded;
        }
    }

    private void OnTileChanged(Entity<BulkMiningConnectivityComponent> ent, ref TileChangedEvent args)
    {
        var changed = false;
        var preserved = false;
        foreach (var change in args.Changes)
        {
            if (change.OldTile.IsEmpty == change.NewTile.IsEmpty)
                continue;

            changed = true;
            // Removing a leaf cannot change paths between other vertices. Only its neighbor loses one part.
            // Batch edits and interrupted analyses use full invalidation instead.
            preserved = args.Changes.Length == 1 && change.NewTile.IsEmpty && TryPruneLeaf(ent.Comp, change.GridIndices);
            UpdateNeighbors(ent.Comp, change.GridIndices, !change.NewTile.IsEmpty);
        }

        if (!changed || preserved)
            return;

        // Synchronous invalidation protects the next laser, including another console in the same tick.
        ent.Comp.Complete = false;
        ent.Comp.Pending = false;
        ent.Comp.Stack.Clear();
    }

    private static bool TryPruneLeaf(BulkMiningConnectivityComponent comp, Vector2i tile)
    {
        if (!comp.Complete || !comp.NodeIndices.TryGetValue(tile, out var index))
            return false;

        ref var node = ref GetNode(comp, index);
        if (node.Generation != comp.Generation || !node.NeighborsKnown || (node.Neighbors & (node.Neighbors - 1)) != 0)
            return false;

        if (node.Neighbors == 0)
            return true;

        var neighbor = Neighbor(tile, BitOperations.TrailingZeroCount((uint)node.Neighbors));
        if (!comp.NodeIndices.TryGetValue(neighbor, out var neighborIndex))
            return false;

        ref var next = ref GetNode(comp, neighborIndex);
        if (next.Generation != comp.Generation || next.Parts == 0)
            return false;

        next.Parts--;
        return true;
    }

    private static void UpdateNeighbors(BulkMiningConnectivityComponent comp, Vector2i tile, bool occupied)
    {
        if (comp.NodeIndices.TryGetValue(tile, out var index))
        {
            ref var node = ref GetNode(comp, index);
            node.Generation = 0;
            node.NeighborsKnown = false;
        }

        for (var direction = 0; direction < 4; direction++)
        {
            if (!comp.NodeIndices.TryGetValue(Neighbor(tile, direction), out var neighborIndex))
                continue;

            ref var neighbor = ref GetNode(comp, neighborIndex);
            if (!neighbor.NeighborsKnown)
                continue;

            var bit = 1 << ((direction + 2) % 4);
            neighbor.Neighbors = (byte)(occupied ? neighbor.Neighbors | bit : neighbor.Neighbors & ~bit);
        }
    }

    private void StartAnalysis(Entity<BulkMiningConnectivityComponent, MapGridComponent> ent, Vector2i root)
    {
        var comp = ent.Comp1;
        comp.Generation++;
        comp.Discovered = 0;
        comp.Complete = false;
        comp.Pending = true;
        comp.Stack.Clear();
        VisitNode(ent, GetNodeIndex(comp, root));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _pending.Clear();
        var query = EntityQueryEnumerator<BulkMiningConnectivityComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var comp, out var grid))
        {
            if (comp.Pending && !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                _pending.Add((uid, comp, grid));
        }

        if (_pending.Count == 0)
        {
            _nextGridIndex = 0;
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var budget = MaxStepsPerUpdate;
        var finished = 0;
        // Keep rotating even when all 80+ grids request an analysis together.
        while (budget > 0 && finished < _pending.Count)
        {
            _nextGridIndex %= _pending.Count;
            var ent = _pending[_nextGridIndex++];
            if (!ent.Comp1.Pending)
            {
                finished++;
                continue;
            }

            finished = 0;
            var steps = Math.Min(StepsPerGrid, budget);
            AdvanceAnalysis(ent, steps);
            budget -= steps;
            if (Stopwatch.GetElapsedTime(started) >= UpdateBudget)
                break;
        }
    }

    private void AdvanceAnalysis(Entity<BulkMiningConnectivityComponent, MapGridComponent> ent, int steps)
    {
        var comp = ent.Comp1;
        // Iterative Tarjan traversal: linear total work, spread across updates without recursion.
        for (var step = 0; step < steps && comp.Stack.Count > 0; step++)
        {
            var index = comp.Stack[^1];
            ref var node = ref GetNode(comp, index);
            if (node.NextNeighbor < 4)
            {
                var direction = node.NextNeighbor++;
                if ((node.Neighbors & (1 << direction)) == 0)
                    continue;

                var nextIndex = GetNeighborIndex(comp, index, ref node, direction);
                ref var next = ref GetNode(comp, nextIndex);
                if (next.Generation != comp.Generation)
                {
                    VisitNode(ent, nextIndex);
                }
                else if (comp.Stack.Count < 2 || nextIndex != comp.Stack[^2])
                {
                    node.Low = Math.Min(node.Low, next.Discovery);
                }

                continue;
            }

            comp.Stack.RemoveAt(comp.Stack.Count - 1);
            if (comp.Stack.Count > 0)
            {
                ref var parent = ref GetNode(comp, comp.Stack[^1]);
                parent.Low = Math.Min(parent.Low, node.Low);
                if (node.Low >= parent.Discovery)
                    parent.Parts++;
            }
        }

        if (comp.Stack.Count == 0)
        {
            comp.Pending = false;
            comp.Complete = true;
        }
    }

    private void VisitNode(Entity<BulkMiningConnectivityComponent, MapGridComponent> ent, int index)
    {
        var comp = ent.Comp1;
        ref var node = ref GetNode(comp, index);
        if (!node.NeighborsKnown)
        {
            node.Neighbors = GetNeighbors(ent.Comp2, node.Tile);
            node.NeighborsKnown = true;
        }

        node.Generation = comp.Generation;
        node.Discovery = ++comp.Discovered;
        node.Low = node.Discovery;
        node.NextNeighbor = 0;
        node.Parts = (byte)(comp.Stack.Count == 0 ? 0 : 1);
        comp.Stack.Add(index);
    }

    private static int GetNodeIndex(BulkMiningConnectivityComponent comp, Vector2i tile)
    {
        if (comp.NodeIndices.TryGetValue(tile, out var index))
            return index;

        index = comp.NodeCount++;
        if ((index & (NodePageSize - 1)) == 0)
            comp.NodePages.Add(new BulkMiningConnectivityNode[NodePageSize]);

        GetNode(comp, index).Tile = tile;
        comp.NodeIndices.Add(tile, index);
        return index;
    }

    private static ref BulkMiningConnectivityNode GetNode(BulkMiningConnectivityComponent comp, int index)
    {
        return ref comp.NodePages[index >> NodePageBits][index & (NodePageSize - 1)];
    }

    private static int GetNeighborIndex(BulkMiningConnectivityComponent comp, int index, ref BulkMiningConnectivityNode node, int direction)
    {
        ref var link = ref GetNeighborLink(ref node, direction);
        if (link == 0)
        {
            var nextIndex = GetNodeIndex(comp, Neighbor(node.Tile, direction));
            link = nextIndex + 1;
            // Each undirected edge needs only one coordinate lookup, even during the first traversal.
            GetNeighborLink(ref GetNode(comp, nextIndex), (direction + 2) % 4) = index + 1;
        }

        // Coordinates never change and indices are never recycled. Occupancy is checked separately.
        return link - 1;
    }

    private static ref int GetNeighborLink(ref BulkMiningConnectivityNode node, int direction)
    {
        switch (direction)
        {
            case 0:
                return ref node.East;
            case 1:
                return ref node.North;
            case 2:
                return ref node.West;
            default:
                return ref node.South;
        }
    }

    private static Vector2i Neighbor(Vector2i tile, int direction)
    {
        return direction switch
        {
            0 => new Vector2i(tile.X + 1, tile.Y),
            1 => new Vector2i(tile.X, tile.Y + 1),
            2 => new Vector2i(tile.X - 1, tile.Y),
            _ => new Vector2i(tile.X, tile.Y - 1),
        };
    }
}
