using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Exodus.Casino;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Casino;

public sealed class RouletteBetsOverlay : Overlay
{
    private static readonly Color[] ChipColors =
    [
        Color.FromHex("#E63946"),
        Color.FromHex("#4CC9F0"),
        Color.FromHex("#FFD166"),
        Color.FromHex("#B983FF"),
        Color.FromHex("#80ED99"),
        Color.FromHex("#FF9F1C"),
        Color.FromHex("#F72585"),
        Color.FromHex("#A8DADC")
    ];

    [Dependency] private IInputManager _input = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly IEntityManager _entity;
    private readonly SharedTransformSystem _transform;
    private readonly Font _amountFont;
    private readonly Font _tooltipFont;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities | OverlaySpace.ScreenSpace;

    public RouletteBetsOverlay(IEntityManager entity)
    {
        IoCManager.InjectDependencies(this);
        _entity = entity;
        _transform = entity.System<SharedTransformSystem>();
        _amountFont = _resources.GetFont("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf", 11);
        _tooltipFont = _resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        ZIndex = 10;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawTotals(args);
            DrawTooltip(args);
            return;
        }

        DrawChips(args);
    }

    private void DrawChips(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entity.AllEntityQueryEnumerator<RouletteVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var roulette, out var transform))
        {
            if (transform.MapID != args.MapId || roulette.WorldBets.Length == 0)
                continue;

            var worldPosition = _transform.GetWorldPosition(transform);
            if (!args.WorldBounds.Enlarged(2f).Contains(worldPosition))
                continue;

            handle.SetTransform(_transform.GetWorldMatrix(uid));
            for (var i = 0; i < roulette.WorldBets.Length; i++)
                DrawChip(handle, roulette.WorldBets, i);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawChip(DrawingHandleWorld handle, RouletteWorldBet[] bets, int index)
    {
        var bet = bets[index];
        var center = GetCellCenter(bet.Type, bet.Number);
        var playerIndex = GetPlayerIndex(bets, index);
        var playerCount = GetPlayerCount(bets, index);
        var row = playerIndex / 3;
        var rowCount = Math.Min(3, playerCount - row * 3);
        var stackOffset = new Vector2((playerIndex % 3 - (rowCount - 1) / 2f) * 0.07f, row * 0.055f);
        var age = Math.Clamp((float) ((_timing.CurTime - bet.PlacedAt).TotalSeconds / 0.35), 0f, 1f);
        var scale = 0.7f + EaseOutBack(age) * 0.3f;
        var radius = 0.075f * scale;
        var position = center + stackOffset;
        var color = ChipColors[bet.PlayerSlot % ChipColors.Length];

        handle.DrawCircle(position + new Vector2(0.025f, -0.025f), radius, Color.Black.WithAlpha(0.55f), true);
        handle.DrawCircle(position, radius, color, true);
        handle.DrawCircle(position, radius * 0.64f, Color.White.WithAlpha(0.72f), false);
        handle.DrawLine(position + new Vector2(-radius * 0.55f, 0f),
            position + new Vector2(radius * 0.55f, 0f),
            Color.White.WithAlpha(0.85f));
    }

    private void DrawTooltip(in OverlayDrawArgs args)
    {
        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid || _ui.MouseGetControl(mouse) is not IViewportControl viewport)
            return;

        var mapPosition = viewport.PixelToMap(mouse.Position);
        var query = _entity.AllEntityQueryEnumerator<RouletteVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var roulette, out var transform))
        {
            if (transform.MapID != mapPosition.MapId || roulette.WorldBets.Length == 0)
                continue;

            var local = Vector2.Transform(mapPosition.Position, _transform.GetInvWorldMatrix(uid));
            if (!TryGetHoveredBet(roulette.WorldBets, local, out var hovered, out var total))
                continue;

            var handle = args.ScreenHandle;
            var target = GetBetName(hovered.Type, hovered.Number);
            var lines = new List<string>
            {
                Loc.GetString("roulette-world-tooltip-title", ("target", target), ("amount", total))
            };
            for (var i = 0; i < roulette.WorldBets.Length; i++)
            {
                var bet = roulette.WorldBets[i];
                if (!SameCell(bet, hovered))
                    continue;

                lines.Add(Loc.GetString("roulette-world-tooltip-entry",
                    ("player", bet.PlayerName),
                    ("amount", bet.Amount)));
            }

            DrawTooltipBox(handle, mouse.Position + new Vector2(18f, 18f), lines);
            return;
        }
    }

    private void DrawTotals(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } viewport)
            return;

        var handle = args.ScreenHandle;
        var query = _entity.AllEntityQueryEnumerator<RouletteVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var roulette, out var transform))
        {
            if (transform.MapID != args.MapId || roulette.WorldBets.Length == 0)
                continue;

            var worldMatrix = _transform.GetWorldMatrix(uid);
            for (var i = 0; i < roulette.WorldBets.Length; i++)
            {
                var bet = roulette.WorldBets[i];
                var alreadyDrawn = false;
                var total = 0;
                for (var j = 0; j < roulette.WorldBets.Length; j++)
                {
                    if (!SameCell(roulette.WorldBets[j], bet))
                        continue;

                    total += roulette.WorldBets[j].Amount;
                    if (j < i)
                        alreadyDrawn = true;
                }

                if (alreadyDrawn)
                    continue;

                var local = GetCellCenter(bet.Type, bet.Number);
                var world = Vector2.Transform(local, worldMatrix);
                var text = Loc.GetString("roulette-world-bet-total", ("amount", total));
                var dimensions = handle.GetDimensions(_amountFont, text, 1f);
                var position = viewport.WorldToScreen(world) - dimensions / 2f + new Vector2(0f, -13f);
                var box = UIBox2.FromDimensions(position - new Vector2(3f, 1f), dimensions + new Vector2(6f, 2f));
                handle.DrawRect(box, Color.FromHex("#101410C8"));
                handle.DrawString(_amountFont, position, text, 1f, Color.White);
            }
        }
    }

    private void DrawTooltipBox(DrawingHandleScreen handle, Vector2 position, List<string> lines)
    {
        var lineHeight = _tooltipFont.GetLineHeight(1f);
        var width = 0f;
        for (var i = 0; i < lines.Count; i++)
            width = MathF.Max(width, handle.GetDimensions(_tooltipFont, lines[i], 1f).X);

        var size = new Vector2(width + 16f, lineHeight * lines.Count + 12f);
        handle.DrawRect(UIBox2.FromDimensions(position, size), Color.FromHex("#101410E8"));
        handle.DrawRect(UIBox2.FromDimensions(position, size), Color.FromHex("#D9B44A"), false);
        for (var i = 0; i < lines.Count; i++)
            handle.DrawString(_tooltipFont, position + new Vector2(8f, 6f + lineHeight * i), lines[i]);
    }

    private static bool TryGetHoveredBet(
        RouletteWorldBet[] bets,
        Vector2 local,
        out RouletteWorldBet hovered,
        out int total)
    {
        hovered = default;
        total = 0;
        var bestDistance = float.MaxValue;
        var found = false;
        for (var i = 0; i < bets.Length; i++)
        {
            var bet = bets[i];
            var center = GetCellCenter(bet.Type, bet.Number);
            var halfSize = GetCellHalfSize(bet.Type, bet.Number);
            var offset = Vector2.Abs(local - center);
            if (offset.X > halfSize.X || offset.Y > halfSize.Y)
                continue;

            var distance = Vector2.DistanceSquared(local, center);
            if (distance >= bestDistance)
                continue;

            hovered = bet;
            bestDistance = distance;
            found = true;
        }

        if (!found)
            return false;

        for (var i = 0; i < bets.Length; i++)
        {
            if (SameCell(bets[i], hovered))
                total += bets[i].Amount;
        }

        return true;
    }

    private static Vector2 GetCellHalfSize(RouletteBetType type, int number)
    {
        if (type == RouletteBetType.Number)
            return number == 0 ? new Vector2(1.72f, 0.12f) : new Vector2(0.14f, 0.105f);

        return type switch
        {
            RouletteBetType.FirstDozen or RouletteBetType.SecondDozen or RouletteBetType.ThirdDozen =>
                new Vector2(0.56f, 0.105f),
            _ => new Vector2(0.28f, 0.105f)
        };
    }

    private static Vector2 GetCellCenter(RouletteBetType type, int number)
    {
        if (type == RouletteBetType.Number)
        {
            if (number == 0)
                return new Vector2(0.90f, 0.59f);

            var column = (number - 1) / 3;
            var row = (number - 1) % 3;
            return new Vector2(-0.68f + column * 0.28125f, -0.09f + row * 0.211f);
        }

        return type switch
        {
            RouletteBetType.Low => new Vector2(-0.54f, -0.59f),
            RouletteBetType.Even => new Vector2(0.03f, -0.59f),
            RouletteBetType.Red => new Vector2(0.59f, -0.59f),
            RouletteBetType.Black => new Vector2(1.15f, -0.59f),
            RouletteBetType.Odd => new Vector2(1.71f, -0.59f),
            RouletteBetType.High => new Vector2(2.28f, -0.59f),
            RouletteBetType.FirstDozen => new Vector2(-0.25f, -0.34f),
            RouletteBetType.SecondDozen => new Vector2(0.87f, -0.34f),
            RouletteBetType.ThirdDozen => new Vector2(2.00f, -0.34f),
            _ => Vector2.Zero
        };
    }

    private static string GetBetName(RouletteBetType type, int number)
    {
        return type == RouletteBetType.Number
            ? Loc.GetString("roulette-bet-number-value", ("number", number))
            : Loc.GetString(type switch
            {
                RouletteBetType.Red => "roulette-bet-red",
                RouletteBetType.Black => "roulette-bet-black",
                RouletteBetType.Even => "roulette-bet-even",
                RouletteBetType.Odd => "roulette-bet-odd",
                RouletteBetType.Low => "roulette-bet-low",
                RouletteBetType.High => "roulette-bet-high",
                RouletteBetType.FirstDozen => "roulette-bet-first-dozen",
                RouletteBetType.SecondDozen => "roulette-bet-second-dozen",
                RouletteBetType.ThirdDozen => "roulette-bet-third-dozen",
                _ => "roulette-bet-number"
            });
    }

    private static bool SameCell(RouletteWorldBet left, RouletteWorldBet right)
    {
        return left.Type == right.Type &&
               (left.Type != RouletteBetType.Number || left.Number == right.Number);
    }

    private static int GetPlayerIndex(RouletteWorldBet[] bets, int index)
    {
        var playerIndex = 0;
        for (var i = 0; i < index; i++)
        {
            if (SameCell(bets[i], bets[index]))
                playerIndex++;
        }

        return playerIndex;
    }

    private static int GetPlayerCount(RouletteWorldBet[] bets, int index)
    {
        var playerCount = 0;
        for (var i = 0; i < bets.Length; i++)
        {
            if (SameCell(bets[i], bets[index]))
                playerCount++;
        }

        return playerCount;
    }

    private static float EaseOutBack(float value)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var shifted = value - 1f;
        return 1f + c3 * shifted * shifted * shifted + c1 * shifted * shifted;
    }
}
