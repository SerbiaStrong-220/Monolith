namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>Quarter turns around the anchored origin tile, shared by validation, fixtures and the preview.</summary>
public static class RotGeometry
{
    public static Vector2i Rotate(Vector2i offset, int rotation) => (rotation & 3) switch
    {
        1 => new(-offset.Y, offset.X),
        2 => -offset,
        3 => new(offset.Y, -offset.X),
        _ => offset,
    };

    public static int QuarterTurns(Angle angle) => ((int)Math.Round(angle.Theta / (Math.PI / 2)) % 4 + 4) % 4;

    public static IEnumerable<Vector2i> Cells(Vector2i origin, Vector2i size, int rotation)
    {
        for (var x = 0; x < size.X; x++)
        {
            for (var y = 0; y < size.Y; y++)
                yield return origin + Rotate(new Vector2i(x, y), rotation);
        }
    }
}
