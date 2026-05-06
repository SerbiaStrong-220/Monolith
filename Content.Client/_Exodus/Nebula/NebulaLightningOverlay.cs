using System.Numerics;
using Content.Client.Parallax;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._Exodus.Nebula;

public sealed class NebulaLightningOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private readonly NebulaParallaxSystem _nebulaParallax;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public NebulaLightningOverlay()
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 1;
        IoCManager.InjectDependencies(this);
        _nebulaParallax = _entManager.System<NebulaParallaxSystem>();
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
        worldHandle.UseShader(null);
        worldHandle.DrawRect(args.WorldAABB, new Color(1f, 0.2f, 0.1f, 0.055f * alpha));

        for (var i = 0; i < lightning.PointCount - 1; i++)
        {
            DrawLightningSegment(args, lightning.Points[i], lightning.Points[i + 1], alpha);
        }

        for (var i = 0; i < lightning.BranchCount; i++)
        {
            var branchIndex = i * 2;
            DrawLightningSegment(args, lightning.Branches[branchIndex], lightning.Branches[branchIndex + 1], alpha * 0.65f);
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
        var glow = new Color(1f, 0.08f, 0.04f, 0.36f * alpha);
        var core = new Color(1f, 0.9f, 0.72f, alpha);
        var offset = new Vector2(0.085f, 0.085f);

        worldHandle.DrawLine(start - offset, end - offset, glow);
        worldHandle.DrawLine(start + offset, end + offset, glow);
        worldHandle.DrawLine(start - offset * 0.45f, end - offset * 0.45f, glow);
        worldHandle.DrawLine(start + offset * 0.45f, end + offset * 0.45f, glow);
        worldHandle.DrawLine(start, end, core);
    }

    private static Vector2 ToWorld(Box2 bounds, Vector2 normalized)
    {
        return new Vector2(
            bounds.Left + (bounds.Right - bounds.Left) * normalized.X,
            bounds.Bottom + (bounds.Top - bounds.Bottom) * normalized.Y);
    }
}
