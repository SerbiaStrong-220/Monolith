using System.Globalization;
using System.Numerics;
using System.Text;
using Content.Shared._Mono.Company;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Territory;

/// <summary>
/// Shared corporate border rendering for the mass scanner and BSS map.
/// Geometry and glyph metrics are reused between frames and between territories.
/// </summary>
public sealed class CorporateTerritoryRingRenderer
{
    [Dependency] private readonly ILocalizationManager _localization = default!;

    private readonly Dictionary<string, LabelLayout> _labels = new();
    private Vector2[] _bandVertices = [];
    private Vector2[] _borderVertices = [];
    private Font? _font;
    private float _fontScale;
    private CultureInfo? _culture;

    public CorporateTerritoryRingRenderer()
    {
        IoCManager.InjectDependencies(this);
    }

    public void Draw(
        DrawingHandleScreen handle,
        Font font,
        Vector2 center,
        float radius,
        ProtoId<CompanyPrototype>? company,
        IPrototypeManager prototypes,
        float uiScale,
        Box2 viewBounds,
        float ringScale = 1f)
    {
        if (company is not { } companyId ||
            !prototypes.TryIndex(companyId, out var prototype) ||
            radius <= 0f || !float.IsFinite(radius))
        {
            return;
        }

        var height = font.GetHeight(uiScale);
        var width = Math.Clamp(radius * 0.25f, 2f * uiScale, height + 8f * uiScale);
        var textAlpha = Math.Clamp((width - height) / (4f * uiScale), 0f, 1f);
        width *= ringScale;
        var outerRadius = radius + width;
        if (!IntersectsView(center, radius, outerRadius, viewBounds))
            return;

        DrawBand(handle, center, radius, outerRadius, prototype.Color);

        if (textAlpha <= 0f)
            return;

        if (_font != font || _fontScale != uiScale || _culture != _localization.DefaultCulture)
        {
            _labels.Clear();
            _font = font;
            _fontScale = uiScale;
            _culture = _localization.DefaultCulture;
        }

        if (!_labels.TryGetValue(prototype.Name, out var layout))
        {
            var text = _localization.TryGetString(prototype.Name, out var localized) ? localized : prototype.Name;
            layout = new LabelLayout(font, text, uiScale);
            _labels.Add(prototype.Name, layout);
        }

        DrawLabel(handle, font, center, radius + width * 0.5f, layout, uiScale,
            Color.InterpolateBetween(prototype.Color, Color.White, 0.4f).WithAlpha(0.8f * textAlpha), viewBounds, ringScale);
    }

    /// <summary>
    /// Cull the annulus itself, including views entirely inside the territory.
    /// </summary>
    public static bool IntersectsView(Vector2 center, float innerRadius, float outerRadius, Box2 viewBounds)
    {
        var closest = Vector2.Clamp(center, viewBounds.BottomLeft, viewBounds.TopRight);
        var farthest = Vector2.Max(Vector2.Abs(viewBounds.BottomLeft - center), Vector2.Abs(viewBounds.TopRight - center));
        return Vector2.DistanceSquared(center, closest) <= outerRadius * outerRadius &&
               farthest.LengthSquared() >= innerRadius * innerRadius;
    }

    private void DrawBand(DrawingHandleScreen handle, Vector2 center, float innerRadius, float outerRadius, Color color)
    {
        // Limit the chord error to about half a pixel at normal radar zoom levels.
        var segments = (int)Math.Clamp(MathF.Ceiling(MathF.PI * MathF.Sqrt(outerRadius)), 48f, 1024f);
        var count = segments + 1;
        if (_borderVertices.Length < count)
        {
            var capacity = Math.Max(count, _borderVertices.Length * 2);
            _borderVertices = new Vector2[capacity];
            _bandVertices = new Vector2[capacity * 2];
        }

        for (var i = 0; i <= segments; i++)
        {
            var angle = MathF.Tau * (i % segments) / segments;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            _bandVertices[i * 2] = center + direction * innerRadius;
            _bandVertices[i * 2 + 1] = center + direction * outerRadius;
            _borderVertices[i] = _bandVertices[i * 2 + 1];
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _bandVertices.AsSpan(0, count * 2), color.WithAlpha(0.12f));
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _borderVertices.AsSpan(0, count), color.WithAlpha(0.65f));
    }

    private static void DrawLabel(
        DrawingHandleScreen handle,
        Font font,
        Vector2 center,
        float radius,
        LabelLayout layout,
        float uiScale,
        Color color,
        Box2 viewBounds,
        float ringScale)
    {
        if (layout.Width <= 0f)
            return;

        var circumference = MathF.Tau * radius;
        // Scale the cached glyphs with the band instead of rebuilding font metrics during zoom.
        var fit = MathF.Min(ringScale, circumference * 0.8f / layout.Width);
        if (fit < 0.65f * ringScale)
            return;

        var labelWidth = layout.Width * fit;
        var repetitions = Math.Clamp((int)(circumference / (labelWidth + 64f * uiScale)), 1, 8);
        var baseline = (font.GetAscent(uiScale) - font.GetDescent(uiScale)) * 0.5f;
        var bounds = viewBounds.Enlarged(font.GetHeight(uiScale));
        var previousTransform = handle.GetTransform();

        try
        {
            for (var repeat = 0; repeat < repetitions; repeat++)
            {
                var middleAngle = -MathF.PI * 0.5f + MathF.Tau * repeat / repetitions;
                // Read the lower half from left to right instead of turning its letters upside down.
                var direction = MathF.Sin(middleAngle) > 0.01f ? -1f : 1f;
                var advance = -layout.Width * 0.5f;
                foreach (var glyph in layout.Glyphs)
                {
                    var angle = middleAngle + direction * (advance + glyph.Advance * 0.5f) * fit / radius;
                    advance += glyph.Advance;
                    var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    if (!bounds.Contains(position))
                        continue;

                    var transform = Matrix3x2.CreateScale(fit) *
                                    Matrix3x2.CreateRotation(angle + direction * MathF.PI * 0.5f) *
                                    Matrix3x2.CreateTranslation(position) * previousTransform;
                    handle.SetTransform(transform);
                    font.DrawChar(handle, glyph.Rune, new Vector2(-glyph.Advance * 0.5f, baseline), uiScale, color);
                }
            }
        }
        finally
        {
            handle.SetTransform(previousTransform);
        }
    }

    private sealed class LabelLayout
    {
        public readonly List<Glyph> Glyphs = new();
        public readonly float Width;

        public LabelLayout(Font font, string text, float scale)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (!font.TryGetCharMetrics(rune, scale, out var metrics))
                    continue;

                Glyphs.Add(new Glyph(rune, metrics.Advance));
                Width += metrics.Advance;
            }
        }
    }

    private readonly record struct Glyph(Rune Rune, float Advance);
}
