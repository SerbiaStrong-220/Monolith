using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.UserInterface.Controls;

/// <summary>
/// Reveals rich text using the original label's layout, without reparsing or reflowing it every frame.
/// Only textured glyphs are cached; unsupported drawing and inline controls use the normal renderer.
/// </summary>
public sealed class TypewriterRichTextLabel : RichTextLabel
{
    private GlyphCaptureHandle? _capture;
    private bool _layoutDirty = true;

    /// <summary>
    /// Fraction of the text to display, from zero to one. Does not affect the measured size.
    /// </summary>
    public float RevealProgress { get; set; }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _layoutDirty = true;
        return base.MeasureOverride(availableSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (RevealProgress >= 1f)
        {
            base.Draw(handle);
            return;
        }

        _capture ??= new GlyphCaptureHandle();
        if (_layoutDirty)
        {
            _capture.Reset();
            base.Draw(_capture);
            _layoutDirty = false;
        }

        if (_capture.UnsupportedDrawing || ChildCount > 0)
        {
            base.Draw(handle);
            return;
        }

        var count = (int) (Math.Clamp(RevealProgress, 0f, 1f) * _capture.Glyphs.Count);
        for (var i = 0; i < count; i++)
        {
            var glyph = _capture.Glyphs[i];
            handle.DrawTextureRectRegion(glyph.Texture, glyph.Rect, glyph.Region, glyph.Color);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _capture?.Dispose();
        base.Dispose(disposing);
    }

    private readonly record struct Glyph(Texture Texture, UIBox2 Rect, UIBox2? Region, Color Color);

    /// <summary>
    /// Captures the glyph quads produced by RichTextLabel once per layout. No render target is allocated.
    /// </summary>
    private sealed class GlyphCaptureHandle() : DrawingHandleScreen(Texture.White)
    {
        public readonly List<Glyph> Glyphs = new();
        public bool UnsupportedDrawing { get; private set; }

        public void Reset()
        {
            Glyphs.Clear();
            Modulate = Color.White;
            UnsupportedDrawing = false;
        }

        public override void DrawTextureRectRegion(Texture texture, UIBox2 rect, UIBox2? subRegion = null, Color? modulate = null)
        {
            Glyphs.Add(new Glyph(texture, rect, subRegion, (modulate ?? Color.White) * Modulate));
        }

        public override void SetTransform(in Matrix3x2 matrix)
        {
            UnsupportedDrawing = true;
        }

        public override Matrix3x2 GetTransform()
        {
            UnsupportedDrawing = true;
            return Matrix3x2.Identity;
        }

        public override void UseShader(ShaderInstance? shader)
        {
            UnsupportedDrawing = true;
        }

        public override ShaderInstance? GetShader()
        {
            UnsupportedDrawing = true;
            return null;
        }

        public override void DrawRect(UIBox2 rect, Color color, bool filled = true)
        {
            UnsupportedDrawing = true;
        }

        public override void DrawLine(Vector2 from, Vector2 to, Color color)
        {
            UnsupportedDrawing = true;
        }

        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<DrawVertexUV2DColor> vertices)
        {
            UnsupportedDrawing = true;
        }

        public override void DrawPrimitives(DrawPrimitiveTopology primitiveTopology, Texture texture, ReadOnlySpan<ushort> indices, ReadOnlySpan<DrawVertexUV2DColor> vertices)
        {
            UnsupportedDrawing = true;
        }

        public override void RenderInRenderTarget(IRenderTarget target, Action a, Color? clearColor)
        {
            UnsupportedDrawing = true;
        }

        public override void DrawEntity(EntityUid entity,
            Vector2 position,
            Vector2 scale,
            Angle? worldRot,
            Angle eyeRotation = default,
            Direction? overrideDirection = null,
            SpriteComponent? sprite = null,
            TransformComponent? xform = null,
            SharedTransformSystem? xformSystem = null)
        {
            UnsupportedDrawing = true;
        }
    }
}
