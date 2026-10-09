using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._Exodus.Shuttles.UI;

/// <summary>
/// Draws hollow polygon markers without allocating per grid or filling their centre.
/// </summary>
public sealed class BluespaceMapIconRenderer
{
    private const int MaxSides = 32;
    private readonly Vector2[] _vertices = new Vector2[(MaxSides + 1) * 2];

    public void Draw(DrawingHandleScreen handle, Vector2 center, float radius, int sides, float innerRadius, Color color)
    {
        sides = Math.Clamp(sides, 3, MaxSides);
        innerRadius = float.IsFinite(innerRadius) ? Math.Clamp(innerRadius, 0.05f, 0.95f) : 0.6f;

        for (var i = 0; i < sides; i++)
        {
            var angle = -MathF.PI / 2f + MathF.Tau * i / sides;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            _vertices[i * 2] = center + offset;
            _vertices[i * 2 + 1] = center + offset * innerRadius;
        }

        var count = sides * 2;
        _vertices[count] = _vertices[0];
        _vertices[count + 1] = _vertices[1];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _vertices.AsSpan(0, count + 2), color);
    }
}
