using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>A curved medical shield with two contrasting serpents and alternating over/under crossings.</summary>
public sealed class MedicalTrackingEmblem : Control
{
    private const int CurveSteps = 16;
    private static readonly Vector2[] ShieldCurve =
    [
        new(8, 4),
        new(13, 2), new(27, 2), new(32, 4),
        new(35, 5), new(35, 5), new(35, 9),
        new(35, 25), new(30, 31), new(20, 36),
        new(10, 31), new(5, 25), new(5, 9),
        new(5, 5), new(5, 5), new(8, 4),
    ];
    private static readonly Vector2[] SnakeCurve =
    [
        new(13, 10.5f),
        new(5, 11), new(9, 14), new(20, 19),
        new(30, 23), new(27, 25), new(20, 27),
        new(14, 29), new(15, 31), new(19, 33),
    ];

    private readonly List<(Vector2[] Vertices, Vector2[] Scaled, Color Color, DrawPrimitiveTopology Topology)> _meshes = new();
    private float _drawScale;

    public MedicalTrackingEmblem()
    {
        MinSize = MaxSize = new Vector2(40, 40);
        MouseFilter = MouseFilterMode.Ignore;
        var background = MedicalTrackingUiTheme.Background;
        var shield = SampleCurve(ShieldCurve);
        AddMesh(shield, background, DrawPrimitiveTopology.TriangleFan);
        AddStroke(shield, MedicalTrackingUiTheme.Border, 0.8f);

        // Subtle saltire preserves the banner motif without obscuring the snakes.
        AddStroke([new(10, 10), new(30, 28)], MedicalTrackingUiTheme.Border.WithAlpha(0.45f), 1.1f);
        AddStroke([new(30, 10), new(10, 28)], MedicalTrackingUiTheme.Border.WithAlpha(0.45f), 1.1f);
        AddStroke([new(20, 7), new(20, 32)], MedicalTrackingUiTheme.Muted, 1f);
        AddEllipse(new Vector2(20, 6.5f), new Vector2(1.4f), MedicalTrackingUiTheme.Muted);

        var left = SampleCurve(SnakeCurve);
        var right = new Vector2[left.Length];
        for (var i = 0; i < left.Length; i++)
            right[i] = new Vector2(40 - left[i].X, left[i].Y);

        AddStroke(left, background, 3.8f, taper: true);
        AddStroke(left, MedicalTrackingUiTheme.Accent, 2.2f, taper: true);
        AddStroke(right, background, 3.8f, taper: true);
        AddStroke(right, MedicalTrackingUiTheme.Text, 2.2f, taper: true);
        // Left snake passes above at the first crossing; right snake above at the second.
        AddStroke(left, background, 3.8f, start: CurveSteps - 3, end: CurveSteps + 3);
        AddStroke(left, MedicalTrackingUiTheme.Accent, 2.2f, start: CurveSteps - 3, end: CurveSteps + 3);

        for (var side = 0; side < 2; side++)
        {
            var mirror = side == 0 ? 1f : -1f;
            var color = side == 0 ? MedicalTrackingUiTheme.Accent : MedicalTrackingUiTheme.Text;
            var head = new Vector2(20 - 6.8f * mirror, 9.8f);
            // Distinct heads, eyes and inward-facing snouts make the two strands readable as snakes.
            AddEllipse(head, new Vector2(2.6f, 1.7f), color);
            AddEllipse(head + new Vector2(1.8f * mirror, -0.2f), new Vector2(1.3f, 1f), color);
            AddEllipse(head + new Vector2(0.8f * mirror, -0.65f), new Vector2(0.48f), background);
            AddStroke([head + new Vector2(2.5f * mirror, 0.45f), head + new Vector2(3.7f * mirror, 0.45f)], color, 0.55f);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        // Geometry is created once and only rescaled when the UI scale changes.
        var rescale = !_drawScale.Equals(UIScale);
        foreach (var (vertices, scaled, color, topology) in _meshes)
        {
            if (rescale)
            {
                for (var i = 0; i < vertices.Length; i++)
                    scaled[i] = vertices[i] * UIScale;
            }
            handle.DrawPrimitives(topology, scaled.AsSpan(), color);
        }
        _drawScale = UIScale;
    }

    private static Vector2[] SampleCurve(Vector2[] controlPoints)
    {
        var segments = (controlPoints.Length - 1) / 3;
        var points = new Vector2[segments * CurveSteps + 1];
        for (var segment = 0; segment < segments; segment++)
        {
            var offset = segment * 3;
            for (var step = 0; step <= CurveSteps; step++)
            {
                var t = step / (float) CurveSteps;
                var s = 1f - t;
                points[segment * CurveSteps + step] = s * s * s * controlPoints[offset]
                    + 3f * s * s * t * controlPoints[offset + 1]
                    + 3f * s * t * t * controlPoints[offset + 2]
                    + t * t * t * controlPoints[offset + 3];
            }
        }
        return points;
    }

    private void AddStroke(Vector2[] points, Color color, float width, bool taper = false, int start = 0, int? end = null)
    {
        var last = end ?? points.Length - 1;
        var vertices = new Vector2[(last - start + 1) * 2];
        var closed = points[0] == points[^1];
        for (var i = start; i <= last; i++)
        {
            var before = i == 0 ? (closed ? points[^2] : points[0]) : points[i - 1];
            var after = i == points.Length - 1 ? (closed ? points[1] : points[i]) : points[i + 1];
            var direction = Vector2.Normalize(after - before);
            var thickness = taper ? 1f - 0.8f * Math.Clamp((i - 2f * CurveSteps) / CurveSteps, 0f, 1f) : 1f;
            var normal = new Vector2(-direction.Y, direction.X) * (width * thickness / 2f);
            vertices[(i - start) * 2] = points[i] + normal;
            vertices[(i - start) * 2 + 1] = points[i] - normal;
        }
        AddMesh(vertices, color, DrawPrimitiveTopology.TriangleStrip);
    }

    private void AddEllipse(Vector2 center, Vector2 radius, Color color)
    {
        const int segments = 24;
        var vertices = new Vector2[segments + 2];
        vertices[0] = center;
        for (var i = 0; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            vertices[i + 1] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        AddMesh(vertices, color, DrawPrimitiveTopology.TriangleFan);
    }

    private void AddMesh(Vector2[] vertices, Color color, DrawPrimitiveTopology topology)
    {
        _meshes.Add((vertices, new Vector2[vertices.Length], color, topology));
    }
}
