using System.Numerics;
using Content.Shared._Exodus.Weapons.DistanceFalloff;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Exodus.Effects;

/// <summary>All visible waves share one screen snapshot instead of copying the viewport per projectile.</summary>
public sealed class WaveDistortionOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private readonly SharedTransformSystem _transform;
    private readonly DistanceFalloffSystem _falloff;
    private readonly EntityQuery<DistanceFalloffComponent> _falloffQuery;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly List<VisibleWave> _visible = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => true;

    public WaveDistortionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        _falloff = _entities.System<DistanceFalloffSystem>();
        _falloffQuery = _entities.GetEntityQuery<DistanceFalloffComponent>();
        _spriteQuery = _entities.GetEntityQuery<SpriteComponent>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visible.Clear();
        if (args.Viewport.Eye == null)
            return false;

        var query = _entities.EntityQueryEnumerator<WaveDistortionVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var xform))
        {
            if (visual.Instance == null || xform.MapID != args.MapId ||
                (_spriteQuery.TryComp(uid, out var sprite) && (!sprite.Visible || sprite.ContainerOccluded)) ||
                !float.IsFinite(visual.Size.X) || !float.IsFinite(visual.Size.Y) ||
                visual.Size.X <= 0f || visual.Size.Y <= 0f)
                continue;

            var position = _transform.GetWorldPosition(xform);
            // The diagonal bounds cover all sprite rotations.
            var cullingBounds = Box2.CenteredAround(position, new Vector2(visual.Size.Length()));
            if (!args.WorldAABB.Intersects(cullingBounds))
                continue;

            var strength = 1f;
            if (_falloffQuery.TryComp(uid, out var falloff))
                _falloff.TryGetStrength((uid, falloff), xform.Coordinates, out strength);

            if (strength <= 0f)
                continue;

            var bounds = new Box2Rotated(Box2.CenteredAround(position, visual.Size),
                _transform.GetWorldRotation(xform), position);
            _visible.Add(new VisibleWave(visual, bounds, strength));
        }

        return _visible.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        var handle = args.WorldHandle;
        var renderScale = args.Viewport.RenderScale * args.Viewport.Eye.Scale;
        foreach (var wave in _visible)
        {
            if (wave.Visual.Instance is not { } shader)
                continue;

            shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            shader.SetParameter("strength", wave.Strength);
            shader.SetParameter("intensity", wave.Visual.Intensity);
            shader.SetParameter("opacity", wave.Visual.Opacity);
            shader.SetParameter("frontWidth", wave.Visual.FrontWidth);
            shader.SetParameter("curvature", wave.Visual.Curvature);
            shader.SetParameter("renderScale", renderScale);

            // Convert the local axes to fragment space, including camera/grid rotation and the Y flip.
            var centre = args.Viewport.WorldToLocal(wave.Bounds.Center);
            var axisX = args.Viewport.WorldToLocal(wave.Bounds.Center + wave.Bounds.Rotation.RotateVec(Vector2.UnitX)) - centre;
            var axisY = args.Viewport.WorldToLocal(wave.Bounds.Center + wave.Bounds.Rotation.RotateVec(Vector2.UnitY)) - centre;
            axisX.Y = -axisX.Y;
            axisY.Y = -axisY.Y;
            shader.SetParameter("axisX", Vector2.Normalize(axisX));
            shader.SetParameter("axisY", Vector2.Normalize(axisY));
            handle.UseShader(shader);
            handle.DrawTextureRect(Texture.White, wave.Bounds);
        }

        handle.UseShader(null);
    }

    private readonly record struct VisibleWave(WaveDistortionVisualsComponent Visual, Box2Rotated Bounds, float Strength);
}
