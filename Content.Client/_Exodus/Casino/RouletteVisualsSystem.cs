using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Exodus.Casino;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Casino;

public sealed partial class RouletteVisualsSystem : EntitySystem
{
    private static readonly Vector2 WheelOffset = new(-1.5f, 0.5f);

    private static readonly int[] EuropeanOrder =
    [
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
        5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
    ];

    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;

    private RouletteBetsOverlay _betsOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RouletteVisualsComponent, ComponentStartup>(OnVisualsStartup);
        SubscribeLocalEvent<RouletteVisualsComponent, AfterAutoHandleStateEvent>(OnVisualsState);
        SubscribeLocalEvent<RouletteVisualsComponent, ComponentShutdown>(OnVisualsShutdown);

        _betsOverlay = new RouletteBetsOverlay(EntityManager);
        _overlayManager.AddOverlay(_betsOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<RouletteBetsOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<RouletteVisualsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var sprite))
        {
            if (visuals.Phase == RoulettePhase.Spinning)
                UpdateSprite((uid, visuals, sprite));
        }
    }

    private void OnVisualsStartup(Entity<RouletteVisualsComponent> ent, ref ComponentStartup args)
    {
        _betsOverlay.Update(ent.Owner, ent.Comp.WorldBets);
        if (TryComp<SpriteComponent>(ent, out var sprite))
            UpdateSprite((ent.Owner, ent.Comp, sprite));
    }

    private void OnVisualsState(Entity<RouletteVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _betsOverlay.Update(ent.Owner, ent.Comp.WorldBets);
        if (TryComp<SpriteComponent>(ent, out var sprite))
            UpdateSprite((ent.Owner, ent.Comp, sprite));
    }

    private void OnVisualsShutdown(Entity<RouletteVisualsComponent> ent, ref ComponentShutdown args)
    {
        _betsOverlay.Remove(ent.Owner);
    }

    private void UpdateSprite(Entity<RouletteVisualsComponent, SpriteComponent> ent)
    {
        GetAnimation(ent.Comp1.Phase,
            ent.Comp1.PhaseStartedAt,
            ent.Comp1.PhaseEndsAt,
            ent.Comp1.WinningNumber,
            ent.Comp1.RoundId,
            _timing.CurTime,
            out var wheelAngle,
            out var ballOffset,
            out var highlightVisible);

        var spriteEnt = new Entity<SpriteComponent?>(ent.Owner, ent.Comp2);
        _sprite.LayerSetRotation(spriteEnt, RouletteVisualLayers.Wheel, new Angle(-wheelAngle));
        _sprite.LayerSetOffset(spriteEnt, RouletteVisualLayers.Wheel, WheelOffset);
        var worldBallOffset = new Vector2(ballOffset.X, -ballOffset.Y) * 2f;
        _sprite.LayerSetOffset(spriteEnt, RouletteVisualLayers.Ball, WheelOffset + worldBallOffset);
        _sprite.LayerSetOffset(spriteEnt, RouletteVisualLayers.Highlight, WheelOffset);
        _sprite.LayerSetRotation(spriteEnt, RouletteVisualLayers.Highlight,
            new Angle(-GetResultAngle(ent.Comp1.WinningNumber) - MathF.PI / 2f));
        _sprite.LayerSetVisible(spriteEnt, RouletteVisualLayers.Highlight, highlightVisible);
    }

    public static void GetAnimation(
        RoulettePhase phase,
        TimeSpan phaseStartedAt,
        TimeSpan phaseEndsAt,
        int winningNumber,
        uint roundId,
        TimeSpan now,
        out float wheelAngle,
        out Vector2 ballOffset,
        out bool highlightVisible)
    {
        wheelAngle = 0f;
        ballOffset = PointOnCircle(GetResultAngle(winningNumber), 0.37f);
        highlightVisible = phase == RoulettePhase.Payout && winningNumber >= 0;
        if (phase != RoulettePhase.Spinning || winningNumber < 0)
            return;

        var duration = phaseEndsAt - phaseStartedAt;
        var elapsed = now - phaseStartedAt;
        var progress = duration <= TimeSpan.Zero ? 1f : Math.Clamp((float) (elapsed / duration), 0f, 1f);
        var wheelEased = EaseOutQuint(progress);
        var ballEased = 1f - (1f - progress) * (1f - progress);
        var resultAngle = GetResultAngle(winningNumber);
        var rotations = 6f + roundId % 3;
        var startAngle = (roundId * 17u % 37u) / 37f * MathF.Tau;
        var impact = MathF.Sin(progress * MathF.PI * (8f + roundId % 4)) * progress * (1f - progress);
        var angle = startAngle * (1f - ballEased) - ballEased * MathF.Tau * rotations +
                    resultAngle * ballEased + impact * 0.22f;
        var dropProgress = Math.Clamp((progress - 0.65f) / 0.35f, 0f, 1f);
        var pocketDrop = dropProgress * dropProgress * (3f - 2f * dropProgress);
        var radius = 0.43f - 0.06f * pocketDrop + impact * 0.012f;
        wheelAngle = wheelEased * MathF.Tau * 5f;
        ballOffset = PointOnCircle(angle, radius);
    }

    public static float GetResultAngle(int winningNumber)
    {
        if (winningNumber < 0)
            return 0f;

        for (var i = 0; i < EuropeanOrder.Length; i++)
        {
            if (EuropeanOrder[i] == winningNumber)
                return -MathF.PI / 2f + i * MathF.Tau / EuropeanOrder.Length;
        }

        return 0f;
    }

    public static Vector2 PointOnCircle(float angle, float radius)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static float EaseOutQuint(float value)
    {
        var inverse = 1f - value;
        return 1f - inverse * inverse * inverse * inverse * inverse;
    }
}

public sealed class RouletteWheelControl : Control
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Texture _wheelTexture;
    private readonly Texture _ballTexture;
    private readonly Texture _tableTexture;
    private readonly Texture _highlightTexture;
    private RoulettePhase _phase;
    private TimeSpan _phaseStartedAt;
    private TimeSpan _phaseEndsAt;
    private int _winningNumber = -1;
    private uint _roundId;

    public RouletteWheelControl()
    {
        IoCManager.InjectDependencies(this);
        var resources = IoCManager.Resolve<IResourceCache>();
        var rsi = resources
            .GetResource<RSIResource>("/Textures/_Exodus/Structures/Casino/roulette.rsi")
            .RSI;
        _wheelTexture = rsi["roulette_wheel"].Frame0;
        _ballTexture = rsi["roulette_ball"].Frame0;
        _tableTexture = rsi["roulette_table"].Frame0;
        _highlightTexture = rsi["roulette_highlight"].Frame0;
        RectClipContent = true;
    }

    public void SetState(
        RoulettePhase phase,
        TimeSpan phaseStartedAt,
        TimeSpan phaseEndsAt,
        int winningNumber,
        uint roundId)
    {
        _phase = phase;
        _phaseStartedAt = phaseStartedAt;
        _phaseEndsAt = phaseEndsAt;
        _winningNumber = winningNumber;
        _roundId = roundId;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var center = PixelSize / 2f;
        var diameter = MathF.Min(PixelSize.X, PixelSize.Y);
        var size = new Vector2(diameter, diameter);
        var rect = UIBox2.FromDimensions(center - size / 2f, size);
        handle.DrawTextureRect(_tableTexture, rect);

        RouletteVisualsSystem.GetAnimation(_phase, _phaseStartedAt, _phaseEndsAt, _winningNumber, _roundId,
            _timing.CurTime,
            out var wheelAngle, out var ballOffset, out var highlightVisible);
        var previous = handle.GetTransform();
        handle.SetTransform(GlobalPixelPosition + center, new Angle(wheelAngle), Vector2.One);
        handle.DrawTextureRect(_wheelTexture, UIBox2.FromDimensions(-size / 2f, size));
        handle.SetTransform(previous);

        if (highlightVisible)
        {
            handle.SetTransform(GlobalPixelPosition + center,
                new Angle(RouletteVisualsSystem.GetResultAngle(_winningNumber) + MathF.PI / 2f),
                Vector2.One);
            handle.DrawTextureRect(_highlightTexture, UIBox2.FromDimensions(-size / 2f, size));
            handle.SetTransform(previous);
        }

        var ballSize = new Vector2(diameter * 0.065f);
        handle.DrawTextureRect(_ballTexture,
            UIBox2.FromDimensions(center + ballOffset * diameter - ballSize / 2f, ballSize));
    }
}
