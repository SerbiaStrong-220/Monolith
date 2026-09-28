using Robust.Server.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Tracks finite natural deposits entirely through generation, tile changes and grid splits.
/// No world scans, update loop or client replication are needed.
/// </summary>
public sealed partial class BulkMiningDepositSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    private EntityQuery<BulkMiningDepositComponent> _depositQuery;

    public override void Initialize()
    {
        base.Initialize();
        _depositQuery = GetEntityQuery<BulkMiningDepositComponent>();
        SubscribeLocalEvent<BulkMiningDepositComponent, BulkMiningDepositGeneratedEvent>(OnGenerated);
        SubscribeLocalEvent<BulkMiningDepositComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<BulkMiningDepositComponent, PostGridSplitEvent>(OnGridSplit);
    }

    /// <summary>Whether this grid has captured its natural deposit, including an exhausted one.</summary>
    public bool IsInitialized(EntityUid grid)
    {
        return _depositQuery.TryComp(grid, out var deposit) && deposit.Captured;
    }

    /// <summary>Checks the original-tile mask without enumerating tiles or allocating.</summary>
    public bool CanMine(EntityUid grid, Vector2i tile)
    {
        return _depositQuery.TryComp(grid, out var deposit) && deposit.Captured &&
               deposit.Blocks.TryGetValue(Block(tile), out var bits) && (bits & Bit(tile)) != 0;
    }

    private void OnGenerated(Entity<BulkMiningDepositComponent> ent, ref BulkMiningDepositGeneratedEvent args)
    {
        if (ent.Comp.Captured || TerminatingOrDeleted(ent) || !TryComp<MapGridComponent>(ent, out var grid))
            return;

        ent.Comp.Blocks.Clear();
        var tiles = _map.GetAllTilesEnumerator(ent, grid);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is { } tileRef && !tileRef.Tile.IsEmpty)
                AddTile(ent.Comp, tileRef.GridIndices);
        }

        ent.Comp.Captured = true;
    }

    private void OnTileChanged(Entity<BulkMiningDepositComponent> ent, ref TileChangedEvent args)
    {
        if (!ent.Comp.Captured || ent.Comp.Blocks.Count == 0)
            return;

        foreach (var change in args.Changes)
        {
            // Additions never grant rights. Ignore them also when loading saved tiles.
            // Cosmetic variant/rotation changes do not replace the original substrate.
            if (!change.OldTile.IsEmpty && change.OldTile.TypeId != change.NewTile.TypeId)
                RemoveTile(ent.Comp, change.GridIndices);
        }
    }

    private void OnGridSplit(Entity<BulkMiningDepositComponent> ent, ref PostGridSplitEvent args)
    {
        if (!ent.Comp.Captured || TerminatingOrDeleted(args.Grid) || !TryComp<MapGridComponent>(args.Grid, out var grid))
            return;

        // This event precedes removal of the transferred tiles from the old grid.
        // Inspect only the detached fragment, never rescan the parent for each fragment.
        var deposit = EnsureComp<BulkMiningDepositComponent>(args.Grid);
        deposit.Captured = true;
        var tiles = _map.GetAllTilesEnumerator(args.Grid, grid);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is { } tileRef && !tileRef.Tile.IsEmpty && RemoveTile(ent.Comp, tileRef.GridIndices))
                AddTile(deposit, tileRef.GridIndices);
        }
    }

    private static Vector2i Block(Vector2i tile)
    {
        return new Vector2i(tile.X >> 3, tile.Y >> 3);
    }

    private static ulong Bit(Vector2i tile)
    {
        return 1UL << ((tile.X & 7) + ((tile.Y & 7) << 3));
    }

    private static void AddTile(BulkMiningDepositComponent deposit, Vector2i tile)
    {
        var block = Block(tile);
        deposit.Blocks.TryGetValue(block, out var bits);
        deposit.Blocks[block] = bits | Bit(tile);
    }

    private static bool RemoveTile(BulkMiningDepositComponent deposit, Vector2i tile)
    {
        var block = Block(tile);
        if (!deposit.Blocks.TryGetValue(block, out var bits) || (bits & Bit(tile)) == 0)
            return false;

        bits &= ~Bit(tile);
        if (bits == 0)
            deposit.Blocks.Remove(block);
        else
            deposit.Blocks[block] = bits;

        return true;
    }
}
