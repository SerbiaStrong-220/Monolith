using System.Numerics;
using Content.Client.Parallax;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Nebula.Rendering;

public sealed class NebulaLightningOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private const float BackgroundFlashAlpha = 0.055f;
    private const float BranchAlphaMultiplier = 0.65f;
    private const float GlowAlpha = 0.36f;
    private const float GlowOffset = 0.085f;
    private const float InnerGlowOffsetMultiplier = 0.45f;

    private readonly NebulaParallaxSystem _nebulaParallax;
    private readonly ShaderInstance _unshadedShader;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public NebulaLightningOverlay()
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 1;
        IoCManager.InjectDependencies(this);
        _nebulaParallax = _entManager.System<NebulaParallaxSystem>();
        _unshadedShader = _prototype.Index<ShaderPrototype>("unshaded").Instance();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.MapId != MapId.Nullspace;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        if (!_nebulaParallax.TryGetBackgroundLightning(out var lightning, out var alpha))
            return;

        args.WorldHandle.UseShader(_unshadedShader);
        DrawBackgroundLightning(args, lightning, alpha);
        args.WorldHandle.UseShader(null);
    }

    private static void DrawBackgroundLightning(
        in OverlayDrawArgs args,
        NebulaBackgroundLightning lightning,
        float alpha)
    {
        if (lightning.PointCount < 2 || alpha <= 0f)
            return;

        var worldHandle = args.WorldHandle;
        worldHandle.DrawRect(args.WorldAABB, new Color(1f, 0.2f, 0.1f, BackgroundFlashAlpha * alpha));

        for (var i = 0; i < lightning.PointCount - 1; i++)
        {
            DrawLightningSegment(args, lightning.Points[i], lightning.Points[i + 1], alpha);
        }

        for (var i = 0; i < lightning.BranchCount; i++)
        {
            var branchIndex = i * 2;
            DrawLightningSegment(args, lightning.Branches[branchIndex], lightning.Branches[branchIndex + 1], alpha * BranchAlphaMultiplier);
        }
    }

    private static void DrawLightningSegment(
        in OverlayDrawArgs args,
        Vector2 from,
        Vector2 to,
        float alpha)
    {
        var worldHandle = args.WorldHandle;
        var start = ToWorld(args.WorldAABB, from);
        var end = ToWorld(args.WorldAABB, to);
        var glow = new Color(1f, 0.08f, 0.04f, GlowAlpha * alpha);
        var core = new Color(1f, 0.9f, 0.72f, alpha);
        var offset = new Vector2(GlowOffset, GlowOffset);

        worldHandle.DrawLine(start - offset, end - offset, glow);
        worldHandle.DrawLine(start + offset, end + offset, glow);
        worldHandle.DrawLine(start - offset * InnerGlowOffsetMultiplier, end - offset * InnerGlowOffsetMultiplier, glow);
        worldHandle.DrawLine(start + offset * InnerGlowOffsetMultiplier, end + offset * InnerGlowOffsetMultiplier, glow);
        worldHandle.DrawLine(start, end, core);
    }

    private static Vector2 ToWorld(Box2 bounds, Vector2 normalized)
    {
        return new Vector2(
            bounds.Left + (bounds.Right - bounds.Left) * normalized.X,
            bounds.Bottom + (bounds.Top - bounds.Bottom) * normalized.Y);
    }
}
