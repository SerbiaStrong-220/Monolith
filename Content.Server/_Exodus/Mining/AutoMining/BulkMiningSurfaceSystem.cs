using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Tracks which target tiles of a mined grid are exposed to space. Only exposed tiles can be the first
/// rock a beam meets, and the nearest remaining tile to any outside point is always exposed.
/// </summary>
public sealed partial class BulkMiningSurfaceSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private BulkMiningDepositSystem _deposits = default!;

    public const int BlockSize = 8;

    private EntityQuery<BulkMiningSurfaceComponent> _surfaceQuery;

    public override void Initialize()
    {
        base.Initialize();
        _surfaceQuery = GetEntityQuery<BulkMiningSurfaceComponent>();
        SubscribeLocalEvent<BulkMiningSurfaceComponent, TileChangedEvent>(OnTileChanged);
    }

    /// <summary>Retains the shared surface of a grid, building it once for all consoles mining that grid.</summary>
    public BulkMiningSurfaceComponent? Retain(EntityUid user, Entity<MapGridComponent> grid)
    {
        if (TerminatingOrDeleted(grid))
            return null;

        // A cache released earlier this tick is still pending removal; replace it with a fresh one.
        if (!_surfaceQuery.TryComp(grid, out var surface) || surface.LifeStage > ComponentLifeStage.Running)
        {
            surface = EnsureComp<BulkMiningSurfaceComponent>(grid);
            Build((grid, surface, grid.Comp));
        }

        surface.Users.Add(user);
        return surface;
    }

    public void Release(EntityUid user, EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !_surfaceQuery.TryComp(grid, out var surface))
            return;

        surface.Users.Remove(user);
        if (surface.Users.Count == 0)
            RemCompDeferred<BulkMiningSurfaceComponent>(grid);
    }

    public static bool IsTarget(BulkMiningSurfaceComponent surface, Vector2i tile)
    {
        return HasBit(surface.Targets, tile);
    }

    public static bool IsExposed(BulkMiningSurfaceComponent surface, Vector2i tile)
    {
        return HasBit(surface.Exposed, tile);
    }

    /// <summary>Exposed tiles of one 8-by-8 block, one bit per tile.</summary>
    public static bool TryGetExposedBlock(BulkMiningSurfaceComponent surface, Vector2i block, out ulong bits)
    {
        return surface.Exposed.TryGetValue(block, out bits);
    }

    /// <summary>Converts a bit of an exposure block back to grid indices.</summary>
    public static Vector2i GetTile(Vector2i block, int bit)
    {
        return new Vector2i(block.X * BlockSize + (bit & (BlockSize - 1)), block.Y * BlockSize + (bit >> 3));
    }

    /// <summary>Distance from a grid-local point to the bounds of an exposure block.</summary>
    public static float GetBlockDistance(Vector2i block, float tileSize, Vector2 local)
    {
        var min = new Vector2(block.X, block.Y) * (BlockSize * tileSize);
        var max = min + new Vector2(BlockSize * tileSize);
        return Vector2.Distance(local, Vector2.Clamp(local, min, max));
    }

    /// <summary>Whether any remaining target tile center lies within range of a grid-local point.</summary>
    public static bool HasExposedTileInRange(BulkMiningSurfaceComponent surface, float tileSize, Vector2 local, float range)
    {
        var rangeSquared = range * range;
        foreach (var (block, bits) in surface.Exposed)
        {
            if (GetBlockDistance(block, tileSize, local) > range)
                continue;

            var remaining = bits;
            while (remaining != 0)
            {
                var bit = BitOperations.TrailingZeroCount(remaining);
                remaining &= remaining - 1;
                var tile = GetTile(block, bit);
                var center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * tileSize;
                if (Vector2.DistanceSquared(local, center) <= rangeSquared)
                    return true;
            }
        }

        return false;
    }

    private void Build(Entity<BulkMiningSurfaceComponent, MapGridComponent> ent)
    {
        var surface = ent.Comp1;
        surface.Targets.Clear();
        surface.Exposed.Clear();
        surface.Remaining = 0;
        surface.Natural = _deposits.IsInitialized(ent);

        // Unknown grids remain targetable for the overload penalty; natural grids expose only original tiles.
        var tiles = _map.GetAllTilesEnumerator(ent, ent.Comp2);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } tileRef || tileRef.Tile.IsEmpty ||
                surface.Natural && !_deposits.CanMine(ent, tileRef.GridIndices))
            {
                continue;
            }

            SetBit(surface.Targets, tileRef.GridIndices, true);
            surface.Remaining++;
        }

        foreach (var (block, bits) in surface.Targets)
        {
            var remaining = bits;
            while (remaining != 0)
            {
                var bit = BitOperations.TrailingZeroCount(remaining);
                remaining &= remaining - 1;
                var tile = GetTile(block, bit);
                if (HasOpenSide(surface, tile))
                    SetBit(surface.Exposed, tile, true);
            }
        }
    }

    private void OnTileChanged(Entity<BulkMiningSurfaceComponent> ent, ref TileChangedEvent args)
    {
        var surface = ent.Comp;
        foreach (var change in args.Changes)
        {
            var tile = change.GridIndices;
            var was = HasBit(surface.Targets, tile);
            // Mirrors the deposit rules: additions never grant slurry and a replaced substrate is consumed.
            var now = !change.NewTile.IsEmpty &&
                      (!surface.Natural || was && change.OldTile.TypeId == change.NewTile.TypeId);
            if (was == now)
                continue;

            SetBit(surface.Targets, tile, now);
            surface.Remaining += now ? 1 : -1;
            UpdateExposure(surface, tile);
            UpdateExposure(surface, tile + new Vector2i(1, 0));
            UpdateExposure(surface, tile + new Vector2i(-1, 0));
            UpdateExposure(surface, tile + new Vector2i(0, 1));
            UpdateExposure(surface, tile + new Vector2i(0, -1));
        }
    }

    private static void UpdateExposure(BulkMiningSurfaceComponent surface, Vector2i tile)
    {
        SetBit(surface.Exposed, tile, HasBit(surface.Targets, tile) && HasOpenSide(surface, tile));
    }

    private static bool HasOpenSide(BulkMiningSurfaceComponent surface, Vector2i tile)
    {
        return !HasBit(surface.Targets, tile + new Vector2i(1, 0)) ||
               !HasBit(surface.Targets, tile + new Vector2i(-1, 0)) ||
               !HasBit(surface.Targets, tile + new Vector2i(0, 1)) ||
               !HasBit(surface.Targets, tile + new Vector2i(0, -1));
    }

    private static Vector2i Block(Vector2i tile)
    {
        return new Vector2i(tile.X >> 3, tile.Y >> 3);
    }

    private static ulong Bit(Vector2i tile)
    {
        return 1UL << ((tile.X & 7) + ((tile.Y & 7) << 3));
    }

    private static bool HasBit(Dictionary<Vector2i, ulong> blocks, Vector2i tile)
    {
        return blocks.TryGetValue(Block(tile), out var bits) && (bits & Bit(tile)) != 0;
    }

    private static void SetBit(Dictionary<Vector2i, ulong> blocks, Vector2i tile, bool value)
    {
        var block = Block(tile);
        blocks.TryGetValue(block, out var bits);
        var updated = value ? bits | Bit(tile) : bits & ~Bit(tile);
        if (updated == bits)
            return;

        if (updated == 0)
            blocks.Remove(block);
        else
            blocks[block] = updated;
    }
}
