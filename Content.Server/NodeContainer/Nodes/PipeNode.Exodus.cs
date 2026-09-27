using Robust.Shared.Map.Components;

namespace Content.Server.NodeContainer.Nodes;

// Exodus: configurable pipe ports on machines larger than one tile.
public partial class PipeNode
{
    /// <summary>Port tile relative to the owner, rotated with its pipe directions.</summary>
    [DataField]
    public Vector2i ConnectionOffset;

    internal EntityUid? OffsetGrid;
    internal Vector2i OffsetTile;

    internal Vector2i GetConnectionTile(Entity<TransformComponent> entity, Entity<MapGridComponent> grid, SharedMapSystem maps)
    {
        var tile = maps.TileIndicesFor(grid, entity.Comp.Coordinates);
        if (ConnectionOffset == Vector2i.Zero)
            return tile;

        var offset = RotationsEnabled ? entity.Comp.LocalRotation.RotateVec(ConnectionOffset) : ConnectionOffset;
        return tile + new Vector2i((int)Math.Round(offset.X), (int)Math.Round(offset.Y));
    }
}
