using System.Numerics;
using Content.Shared._Exodus.Visuals;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Visuals;

/// <summary>
/// Draws temporary event-driven and persistent component-driven visual links between entities.
/// </summary>
public sealed class EntityLinkVisualSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private readonly List<TemporaryEntityLink> _temporaryLinks = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<EntityLinkVisualEvent>(OnEntityLinkVisual);
        _overlayManager.AddOverlay(new EntityLinkVisualOverlay(this, EntityManager, _timing, _prototype));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _temporaryLinks.Clear();
        _overlayManager.RemoveOverlay<EntityLinkVisualOverlay>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_temporaryLinks.Count == 0)
            return;

        var curTime = _timing.CurTime;
        for (var i = _temporaryLinks.Count - 1; i >= 0; i--)
        {
            var link = _temporaryLinks[i];
            if (link.EndTime <= curTime || Deleted(link.Source) || Deleted(link.Target))
                _temporaryLinks.RemoveAt(i);
        }
    }

    private void OnEntityLinkVisual(EntityLinkVisualEvent args)
    {
        if (args.Duration <= TimeSpan.Zero || !_prototype.HasIndex(args.Style))
            return;

        var source = GetEntity(args.Source);
        var target = GetEntity(args.Target);
        if (Deleted(source) || Deleted(target))
            return;

        _temporaryLinks.Add(new TemporaryEntityLink(
            source,
            target,
            args.Style,
            args.SourceOffset,
            args.TargetOffset,
            _timing.CurTime + args.Duration));
    }

    internal IReadOnlyList<TemporaryEntityLink> TemporaryLinks => _temporaryLinks;

    internal readonly record struct TemporaryEntityLink(
        EntityUid Source,
        EntityUid Target,
        ProtoId<EntityLinkVisualPrototype> Style,
        Vector2 SourceOffset,
        Vector2 TargetOffset,
        TimeSpan EndTime);
}

internal sealed class EntityLinkVisualOverlay : Overlay
{
    private readonly EntityLinkVisualSystem _system;
    private readonly IEntityManager _entityManager;
    private readonly IGameTiming _timing;
    private readonly IPrototypeManager _prototype;
    private readonly EntityQuery<TransformComponent> _transformQuery;
    private readonly SharedTransformSystem _transformSystem;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public EntityLinkVisualOverlay(
        EntityLinkVisualSystem system,
        IEntityManager entityManager,
        IGameTiming timing,
        IPrototypeManager prototype)
    {
        _system = system;
        _entityManager = entityManager;
        _timing = timing;
        _prototype = prototype;
        _transformQuery = entityManager.GetEntityQuery<TransformComponent>();
        _transformSystem = entityManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var links = _entityManager.EntityQueryEnumerator<EntityLinkVisualComponent, TransformComponent>();
        while (links.MoveNext(out _, out var link, out var sourceXform))
        {
            if (link.Target is not { } target)
                continue;

            DrawLink(
                args,
                sourceXform,
                target,
                link.Style,
                link.SourceOffset,
                link.TargetOffset,
                1f);
        }

        var curTime = _timing.CurTime;
        var temporaryLinks = _system.TemporaryLinks;
        for (var i = 0; i < temporaryLinks.Count; i++)
        {
            var link = temporaryLinks[i];
            if (!_transformQuery.TryGetComponent(link.Source, out var sourceXform) ||
                !_prototype.TryIndex(link.Style, out var style))
            {
                continue;
            }

            var remaining = link.EndTime - curTime;
            var fade = style.FadeDuration <= TimeSpan.Zero
                ? 1f
                : Math.Clamp((float) (remaining / style.FadeDuration), 0f, 1f);

            DrawLink(
                args,
                sourceXform,
                link.Target,
                style,
                link.SourceOffset,
                link.TargetOffset,
                fade);
        }
    }

    private void DrawLink(
        in OverlayDrawArgs args,
        TransformComponent sourceXform,
        EntityUid target,
        ProtoId<EntityLinkVisualPrototype> styleId,
        Vector2 sourceOffset,
        Vector2 targetOffset,
        float alpha)
    {
        if (!_prototype.TryIndex(styleId, out var style))
            return;

        DrawLink(args, sourceXform, target, style, sourceOffset, targetOffset, alpha);
    }

    private void DrawLink(
        in OverlayDrawArgs args,
        TransformComponent sourceXform,
        EntityUid target,
        EntityLinkVisualPrototype style,
        Vector2 sourceOffset,
        Vector2 targetOffset,
        float alpha)
    {
        if (!_transformQuery.TryGetComponent(target, out var targetXform) ||
            sourceXform.MapID != args.MapId ||
            targetXform.MapID != args.MapId)
        {
            return;
        }

        var sourcePosition = _transformSystem.GetWorldPosition(sourceXform, _transformQuery) +
                             _transformSystem.GetWorldRotation(sourceXform, _transformQuery).RotateVec(sourceOffset);
        var targetPosition = _transformSystem.GetWorldPosition(targetXform, _transformQuery) +
                             _transformSystem.GetWorldRotation(targetXform, _transformQuery).RotateVec(targetOffset);
        var difference = targetPosition - sourcePosition;
        var halfLength = difference.Length() / 2f;
        if (halfLength <= 0f)
            return;

        var midpoint = sourcePosition + difference / 2f;
        var angle = difference.ToWorldAngle();
        var colorAlpha = Math.Clamp(style.Color.A * alpha, 0f, 1f);

        if (style.OuterHalfWidth > 0f && style.OuterAlpha > 0f)
        {
            DrawBeam(
                args.WorldHandle,
                midpoint,
                angle,
                halfLength,
                style.OuterHalfWidth,
                style.Color.WithAlpha(colorAlpha * Math.Clamp(style.OuterAlpha, 0f, 1f)));
        }

        if (style.InnerHalfWidth > 0f && style.InnerAlpha > 0f)
        {
            DrawBeam(
                args.WorldHandle,
                midpoint,
                angle,
                halfLength,
                style.InnerHalfWidth,
                style.Color.WithAlpha(colorAlpha * Math.Clamp(style.InnerAlpha, 0f, 1f)));
        }
    }

    private static void DrawBeam(
        DrawingHandleWorld handle,
        Vector2 midpoint,
        Angle angle,
        float halfLength,
        float halfWidth,
        Color color)
    {
        var box = new Box2(-halfWidth, -halfLength, halfWidth, halfLength);
        var rotated = new Box2Rotated(box.Translated(midpoint), angle, midpoint);
        handle.DrawRect(rotated, color);
    }
}
