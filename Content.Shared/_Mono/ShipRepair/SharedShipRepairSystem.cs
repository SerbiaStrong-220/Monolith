using Content.Shared._Mono.ForceParent;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ForceParentSystem _parent = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private INetManager _net = default!; // .IsServer is kind of a crime but needed to not dupe code
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitTool();
        InitRepairPlans(); // Exodus: shared snapshot work planning for autonomous and area repair.
    }

    /// <summary>
    /// Generate snapshot of grid repair data and store on grid.
    /// </summary>
    // Exodus-begin: build before replacing the active snapshot and invalidate old repair operations.
    public void GenerateRepairData(EntityUid gridUid)
    {
        if (TryCreateRepairData(gridUid, out var snapshot))
            ApplyRepairData((gridUid, EnsureComp<ShipRepairDataComponent>(gridUid)), snapshot);
    }
    // Exodus-end

    public bool TryRepairTileTile(Entity<ShipRepairDataComponent> grid, Vector2i indices)
    {
        if (!TryGetChunk(grid.Comp, indices, out var chunk) || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var relative = GetRelativeIndices(indices, grid.Comp.ChunkSize);
        var idx = relative.X + relative.Y * grid.Comp.ChunkSize;

        var tileToPlace = chunk.Tiles[idx];
        if (tileToPlace != Tile.Empty.TypeId)
            _map.SetTile(grid, gridComp, indices, new Tile(tileToPlace));
        return true;
    }

    protected Vector2i GetRepairChunkIndices(Vector2i gridIndices, int chunkSize)
    {
        var xCoord = gridIndices.X < 0 ? 1 - chunkSize + gridIndices.X : gridIndices.X;
        var yCoord = gridIndices.Y < 0 ? 1 - chunkSize + gridIndices.Y : gridIndices.Y;
        var x = xCoord / chunkSize;
        var y = yCoord / chunkSize;
        return new Vector2i(x, y);
    }

    protected Vector2i GetRelativeIndices(Vector2i gridIndices, int chunkSize)
    {
        var x = MathHelper.Mod(gridIndices.X, chunkSize);
        var y = MathHelper.Mod(gridIndices.Y, chunkSize);
        return new Vector2i(x, y);
    }

    protected ShipRepairChunk GetCreateChunk(ShipRepairDataComponent data, Vector2i gridIndices)
    {
        var chunkSize = data.ChunkSize;
        var chunkIndices = GetRepairChunkIndices(gridIndices, chunkSize);

        if (!data.Chunks.TryGetValue(chunkIndices, out var chunk))
        {
            chunk = new ShipRepairChunk
            {
                Tiles = new int[chunkSize * chunkSize]
            };
            Array.Fill<int>(chunk.Tiles, Tile.Empty.TypeId);
            data.Chunks[chunkIndices] = chunk;
        }

        return chunk;
    }

    protected bool TryGetChunk(ShipRepairDataComponent data, Vector2i gridIndices, [NotNullWhen(true)] out ShipRepairChunk? chunk)
    {
        var chunkSize = data.ChunkSize;
        var chunkIndices = GetRepairChunkIndices(gridIndices, chunkSize);
        return data.Chunks.TryGetValue(chunkIndices, out chunk);
    }
}
