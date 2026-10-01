using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._Exodus.Shuttles.UI;

/// <summary>
/// Draws circles filled with diagonal hatching anchored to their centre, so the pattern pans and zooms with the circle.
/// Only lines crossing the view are built.
/// </summary>
public sealed class HatchedCircleRenderer
{
    private static readonly Vector2 Normal = Vector2.Normalize(new Vector2(1f, 1f));
    private static readonly Vector2 Direction = Vector2.Normalize(new Vector2(1f, -1f));

    private readonly List<Vector2> _lines = new();

    public void DrawHatch(
        DrawingHandleScreen handle,
        Vector2 center,
        float radius,
        float spacing,
        Color color,
        Box2 viewBounds)
    {
        if (spacing <= 0f || radius < spacing || !CircleIntersectsBox(center, radius, viewBounds))
            return;

        var from = -radius;
        var to = radius;
        ClampToBox(center, viewBounds, ref from, ref to);
        if (from > to)
            return;

        _lines.Clear();
        var last = (int) MathF.Floor(to / spacing);
        for (var i = (int) MathF.Ceiling(from / spacing); i <= last; i++)
        {
            var offset = i * spacing;
            var halfLength = MathF.Sqrt(MathF.Max(radius * radius - offset * offset, 0f));
            var middle = center + Normal * offset;
            _lines.Add(middle - Direction * halfLength);
            _lines.Add(middle + Direction * halfLength);
        }

        if (_lines.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineList, _lines, color);
    }

    /// <summary>
    /// Draws a circle's outline in a single batch; the engine's own unfilled circle issues a line per few pixels.
    /// </summary>
    public void DrawOutline(DrawingHandleScreen handle, Vector2 center, float radius, Color color, Box2 viewBounds)
    {
        if (radius <= 0f || !CircleIntersectsBox(center, radius, viewBounds))
            return;

        _lines.Clear();
        var segments = Math.Clamp((int) (radius * 0.25f), 48, 720);
        var previous = center + new Vector2(radius, 0f);
        for (var i = 1; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            _lines.Add(previous);
            _lines.Add(point);
            previous = point;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, _lines, color);
    }

    private static void ClampToBox(Vector2 center, Box2 box, ref float from, ref float to)
    {
        var a = Vector2.Dot(box.BottomLeft - center, Normal);
        var b = Vector2.Dot(box.BottomRight - center, Normal);
        var c = Vector2.Dot(box.TopLeft - center, Normal);
        var d = Vector2.Dot(box.TopRight - center, Normal);
        from = MathF.Max(from, MathF.Min(MathF.Min(a, b), MathF.Min(c, d)));
        to = MathF.Min(to, MathF.Max(MathF.Max(a, b), MathF.Max(c, d)));
    }

    private static bool CircleIntersectsBox(Vector2 center, float radius, Box2 box)
    {
        var closest = new Vector2(
            Math.Clamp(center.X, box.Left, box.Right),
            Math.Clamp(center.Y, box.Bottom, box.Top));

        return (closest - center).LengthSquared() <= radius * radius;
    }
}
