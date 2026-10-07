using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Economy.Admin;

/// <summary>
/// A wrapped warning whose cached glyphs move independently by less than one pixel.
/// Only drawing the visible final confirmation animates it; there is no update subscription.
/// </summary>
public sealed partial class MarketResetWarning : Control
{
    [Dependency] private IGameTiming _timing = default!;
    private readonly List<Glyph> _glyphs = new();
    private string _text = string.Empty;
    private Font? _cachedFont;
    private float _cachedWidth = -1;
    private float _cachedScale;
    private Vector2 _measured;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;

            _text = value;
            _cachedWidth = -1;
            InvalidateMeasure();
        }
    }

    public MarketResetWarning()
    {
        IoCManager.InjectDependencies(this);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        UpdateLayout(availableSize.X);
        return _measured;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        UpdateLayout(finalSize.X);
        return finalSize;
    }

    private void UpdateLayout(float width)
    {
        var font = TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styledFont)
            ? styledFont
            : UserInterfaceManager.ThemeDefaults.LabelFont;
        if (_cachedWidth == width && _cachedScale == UIScale && _cachedFont == font)
            return;

        _cachedWidth = width;
        _cachedScale = UIScale;
        _cachedFont = font;
        _glyphs.Clear();
        var available = Math.Max(1, width * UIScale - 4);
        var baseline = new Vector2(2, font.GetAscent(UIScale) + 2);
        var lineHeight = font.GetLineHeight(UIScale);
        var widest = 0f;
        foreach (var word in _text.Split(' '))
        {
            var wordWidth = 0f;
            foreach (var rune in word.EnumerateRunes())
                wordWidth += font.GetCharMetrics(rune, UIScale)?.Advance ?? 0;

            if (baseline.X > 2 && baseline.X + wordWidth > available)
            {
                baseline.X = 2;
                baseline.Y += lineHeight;
            }

            foreach (var rune in word.EnumerateRunes())
            {
                var advance = font.GetCharMetrics(rune, UIScale)?.Advance ?? 0;
                if (baseline.X > 2 && baseline.X + advance > available)
                {
                    baseline.X = 2;
                    baseline.Y += lineHeight;
                }

                _glyphs.Add(new Glyph(rune, baseline));
                baseline.X += advance;
                widest = Math.Max(widest, baseline.X);
            }

            baseline.X += font.GetCharMetrics(new Rune(' '), UIScale)?.Advance ?? 0;
        }

        _measured = new Vector2((widest + 2) / UIScale, (baseline.Y + font.GetDescent(UIScale) + 2) / UIScale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!VisibleInTree || _cachedFont == null)
            return;

        var time = (float) (_timing.RealTime.TotalSeconds % 1000);
        for (var i = 0; i < _glyphs.Count; i++)
        {
            var glyph = _glyphs[i];
            var offset = new Vector2(MathF.Sin(time * 17 + i * 1.7f), MathF.Sin(time * 21 + i * 2.3f)) * (0.7f * UIScale);
            _cachedFont.DrawChar(handle, glyph.Rune, glyph.Baseline + offset, UIScale, Color.Salmon);
        }
    }

    private readonly record struct Glyph(Rune Rune, Vector2 Baseline);
}
