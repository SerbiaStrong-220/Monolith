// Exodus: consume a floor layer without producing a reusable tile item.
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Maps;

public sealed partial class TileSystem
{
    public bool TryConsumeTile(TileRef tile)
    {
        if (tile.Tile.IsEmpty || !TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return false;
        var attempt = new FloorTileAttemptEvent();
        RaiseLocalEvent(tile.GridUid, ref attempt);
        if (attempt.Cancelled || TryComp<ProtectedGridComponent>(tile.GridUid, out var protection) && protection.PreventFloorRemoval)
            return false;
        var definition = (ContentTileDefinition) _tileDefinitionManager[tile.Tile.TypeId];
        if (string.IsNullOrEmpty(definition.BaseTurf))
            return false;
        var replacement = (ContentTileDefinition) _tileDefinitionManager[definition.BaseTurf];
        if (replacement.TileId == tile.Tile.TypeId)
            return false;
        return ReplaceTile(tile, replacement, tile.GridUid, grid);
    }
}
