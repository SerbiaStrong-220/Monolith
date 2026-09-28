// Exodus: large organic camera ranges gather blockers once, instead of one spatial query per tile.
namespace Content.Shared.Silicons.StationAi;

public sealed partial class StationAiVisionSystem
{
    private void GatherNetworkOccluders(EntityUid grid, Box2 area, float tileSize)
    {
        _occluders.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, area, _occluders, flags: LookupFlags.Static | LookupFlags.Approximate);
        var gridAngle = _xforms.GetWorldRotation(grid);
        foreach (var (uid, occluder) in _occluders)
        {
            if (!occluder.Enabled)
                continue;
            var xform = Transform(uid);
            var position = xform.ParentUid == grid ? xform.LocalPosition
                : _xforms.ToCoordinates(grid, _xforms.GetMapCoordinates(uid, xform)).Position;
            var rotation = xform.ParentUid == grid ? xform.LocalRotation : _xforms.GetWorldRotation(uid) - gridAngle;
            var box = _lookup.GetAABBNoContainer(uid, position, rotation);
            var left = Math.Max((int)Math.Floor(area.Left / tileSize), (int)Math.Floor((box.Left + 0.05f) / tileSize));
            var right = Math.Min((int)Math.Floor(area.Right / tileSize), (int)Math.Floor((box.Right - 0.05f) / tileSize));
            var bottom = Math.Max((int)Math.Floor(area.Bottom / tileSize), (int)Math.Floor((box.Bottom + 0.05f) / tileSize));
            var top = Math.Min((int)Math.Floor(area.Top / tileSize), (int)Math.Floor((box.Top - 0.05f) / tileSize));
            for (var x = left; x <= right; x++)
                for (var y = bottom; y <= top; y++)
                    _opaque.Add(new Vector2i(x, y));
        }
    }
}
