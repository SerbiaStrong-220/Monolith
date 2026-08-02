using System.Numerics;
using Content.Shared._Exodus.Construction;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Construction;

/// <summary>
/// Draws short-lived client-side beams for successful remote part exchanges.
/// </summary>
public sealed class PartExchangeVisualSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private readonly List<PartExchangeBeam> _beams = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PartExchangeVisualEvent>(OnPartExchangeVisual);
        _overlayManager.AddOverlay(new PartExchangeVisualOverlay(this, EntityManager, _timing));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _beams.Clear();
        _overlayManager.RemoveOverlay<PartExchangeVisualOverlay>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_beams.Count == 0)
            return;

        var curTime = _timing.CurTime;
        for (var i = _beams.Count - 1; i >= 0; i--)
        {
            var beam = _beams[i];
            if (beam.EndTime <= curTime || Deleted(beam.User) || Deleted(beam.Target))
                _beams.RemoveAt(i);
        }
    }

    private void OnPartExchangeVisual(PartExchangeVisualEvent args)
    {
        if (args.Duration <= TimeSpan.Zero)
            return;

        var user = GetEntity(args.User);
        var target = GetEntity(args.Target);
        if (Deleted(user) || Deleted(target))
            return;

        _beams.Add(new PartExchangeBeam(user, target, args.Color, _timing.CurTime + args.Duration));
    }

    internal IReadOnlyList<PartExchangeBeam> Beams => _beams;

    internal readonly record struct PartExchangeBeam(
        EntityUid User,
        EntityUid Target,
        Color Color,
        TimeSpan EndTime);
}

internal sealed class PartExchangeVisualOverlay : Overlay
{
    private const float OuterWidth = 0.12f;
    private const float InnerWidth = 0.035f;
    private const float FadeDuration = 0.25f;

    private readonly PartExchangeVisualSystem _system;
    private readonly IGameTiming _timing;
    private readonly EntityQuery<TransformComponent> _transformQuery;
    private readonly SharedTransformSystem _transformSystem;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public PartExchangeVisualOverlay(
        PartExchangeVisualSystem system,
        IEntityManager entityManager,
        IGameTiming timing)
    {
        _system = system;
        _timing = timing;
        _transformQuery = entityManager.GetEntityQuery<TransformComponent>();
        _transformSystem = entityManager.System<SharedTransformSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.Beams.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var curTime = _timing.CurTime;
        foreach (var beam in _system.Beams)
        {
            if (!_transformQuery.TryGetComponent(beam.User, out var userXform) ||
                !_transformQuery.TryGetComponent(beam.Target, out var targetXform) ||
                userXform.MapID != args.MapId ||
                targetXform.MapID != args.MapId)
            {
                continue;
            }

            var userPosition = _transformSystem.GetWorldPosition(userXform, _transformQuery);
            var targetPosition = _transformSystem.GetWorldPosition(targetXform, _transformQuery);
            var difference = targetPosition - userPosition;
            var halfLength = difference.Length() / 2f;
            if (halfLength <= 0f)
                continue;

            var midpoint = userPosition + difference / 2f;
            var angle = difference.ToWorldAngle();
            var remaining = (float) (beam.EndTime - curTime).TotalSeconds;
            var alpha = Math.Clamp(remaining / FadeDuration, 0f, 1f);

            DrawBeam(args.WorldHandle,
                midpoint,
                angle,
                halfLength,
                OuterWidth,
                beam.Color.WithAlpha(beam.Color.A * 0.25f * alpha));
            DrawBeam(args.WorldHandle,
                midpoint,
                angle,
                halfLength,
                InnerWidth,
                beam.Color.WithAlpha(beam.Color.A * 0.9f * alpha));
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
