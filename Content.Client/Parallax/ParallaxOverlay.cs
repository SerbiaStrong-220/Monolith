using System.Numerics;
using Content.Client._Exodus.Nebula;
using Content.Client.Parallax.Managers;
using Content.Shared.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Parallax;

public sealed class ParallaxOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IParallaxManager _manager = default!;
    private readonly ParallaxSystem _parallax;
    private readonly NebulaParallaxSystem _nebulaParallax; // Exodus nebula-parallax

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public ParallaxOverlay()
    {
        ZIndex = ParallaxSystem.ParallaxZIndex;
        IoCManager.InjectDependencies(this);
        _parallax = _entManager.System<ParallaxSystem>();
        _nebulaParallax = _entManager.System<NebulaParallaxSystem>(); // Exodus nebula-parallax
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        if (!_configurationManager.GetCVar(CCVars.ParallaxEnabled))
            return;

        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var worldHandle = args.WorldHandle;

        var layers = _parallax.GetParallaxLayers(args.MapId);
        var realTime = (float) _timing.RealTime.TotalSeconds;

        // Exodus-begin nebula-parallax
        if (_nebulaParallax.TryGetParallaxLayers(out var nebulaLayers, out var nebulaBlend))
        {
            DrawLayers(args, layers, position, realTime);
            if (_nebulaParallax.TryGetBackgroundLightning(out var lightning, out var lightningAlpha))
            {
                var split = Math.Min(NebulaParallaxSystem.BackgroundLightningLayerIndex, nebulaLayers.Length);
                DrawLayers(args, nebulaLayers, position, realTime, nebulaBlend, 0, split);
                DrawBackgroundLightning(args, lightning, lightningAlpha);
                DrawLayers(args, nebulaLayers, position, realTime, nebulaBlend, split, nebulaLayers.Length - split);
            }
            else
            {
                DrawLayers(args, nebulaLayers, position, realTime, nebulaBlend);
            }

            worldHandle.UseShader(null);
            return;
        }
        // Exodus-end

        DrawLayers(args, layers, position, realTime);
        worldHandle.UseShader(null);
    }

    // Exodus-begin nebula-parallax
    private void DrawLayers(
        in OverlayDrawArgs args,
        ParallaxLayerPrepared[] layers,
        Vector2 position,
        float realTime,
        float alpha = 1f,
        int startLayer = 0,
        int layerCount = -1)
    {
        if (alpha <= 0f)
            return;

        var worldHandle = args.WorldHandle;
        Color? modulate = alpha >= 1f ? null : Color.White.WithAlpha(alpha);
        var endLayer = layerCount < 0 ? layers.Length : Math.Min(layers.Length, startLayer + layerCount);

        for (var layerIndex = startLayer; layerIndex < endLayer; layerIndex++)
        {
            var layer = layers[layerIndex];
            ShaderInstance? shader;

            if (!string.IsNullOrEmpty(layer.Config.Shader))
                shader = _prototypeManager.Index<ShaderPrototype>(layer.Config.Shader).Instance();
            else
                shader = null;

            worldHandle.UseShader(shader);
            var tex = layer.Texture;

            // Size of the texture in world units.
            var size = (tex.Size / (float) EyeManager.PixelsPerMeter) * layer.Config.Scale;

            // The "home" position is the effective origin of this layer.
            // Parallax shifting is relative to the home, and shifts away from the home and towards the Eye centre.
            // The effects of this are such that a slowness of 1 anchors the layer to the centre of the screen, while a slowness of 0 anchors the layer to the world.
            // (For values 0.0 to 1.0 this is in effect a lerp, but it's deliberately unclamped.)
            // The ParallaxAnchor adapts the parallax for station positioning and possibly map-specific tweaks.
            var home = layer.Config.WorldHomePosition + _manager.ParallaxAnchor;
            var scrolled = layer.Config.Scrolling * realTime;

            // Origin - start with the parallax shift itself.
            var originBL = (position - home) * layer.Config.Slowness + scrolled;

            // Place at the home.
            originBL += home;

            // Adjust.
            originBL += layer.Config.WorldAdjustPosition;

            // Centre the image.
            originBL -= size / 2;

            if (layer.Config.Tiled)
            {
                // Remove offset so we can floor.
                var flooredBL = args.WorldAABB.BottomLeft - originBL;

                // Floor to background size.
                flooredBL = (flooredBL / size).Floored() * size;

                // Re-offset.
                flooredBL += originBL;

                for (var x = flooredBL.X; x < args.WorldAABB.Right; x += size.X)
                {
                    for (var y = flooredBL.Y; y < args.WorldAABB.Top; y += size.Y)
                    {
                        worldHandle.DrawTextureRect(tex, Box2.FromDimensions(new Vector2(x, y), size), modulate); // Exodus nebula-parallax
                    }
                }
            }
            else
            {
                worldHandle.DrawTextureRect(tex, Box2.FromDimensions(originBL, size), modulate); // Exodus nebula-parallax
            }
        }
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
        worldHandle.DrawRect(args.WorldAABB, new Color(1f, 0.2f, 0.1f, 0.025f * alpha));

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
        var glow = new Color(1f, 0.12f, 0.08f, 0.18f * alpha);
        var core = new Color(1f, 0.82f, 0.62f, 0.9f * alpha);
        var offset = new Vector2(0.055f, 0.055f);

        worldHandle.DrawLine(start - offset, end - offset, glow);
        worldHandle.DrawLine(start + offset, end + offset, glow);
        worldHandle.DrawLine(start, end, core);
    }

    private static Vector2 ToWorld(Box2 bounds, Vector2 normalized)
    {
        return new Vector2(
            bounds.Left + (bounds.Right - bounds.Left) * normalized.X,
            bounds.Bottom + (bounds.Top - bounds.Bottom) * normalized.Y);
    }
    // Exodus-end
}

