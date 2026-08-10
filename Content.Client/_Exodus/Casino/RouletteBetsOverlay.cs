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
    private const int MaxVisibleChipsPerCell = 6;
    private const int MaxVisibleChipsPerTable = 64;
    private const int MaxTooltipEntries = 12;

    private static readonly Vector2 TableOffset = new(0.5f, 0.5f);

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
    private readonly Dictionary<EntityUid, RouletteRenderData> _renderData = new();

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
            DrawScreen(args);
            return;
        }

        DrawChips(args);
    }

    private void DrawChips(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (uid, data) in _renderData)
        {
            if (!_entity.TryGetComponent(uid, out TransformComponent? transform) || transform.MapID != args.MapId)
                continue;

            var worldPosition = _transform.GetWorldPosition(transform);
            if (!args.WorldBounds.Enlarged(4f).Contains(worldPosition))
                continue;

            handle.SetTransform(_transform.GetWorldMatrix(uid));
            for (var i = 0; i < data.Chips.Length; i++)
                DrawChip(handle, data.Chips[i]);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawChip(DrawingHandleWorld handle, RouletteRenderChip chip)
    {
        var age = Math.Clamp((float) ((_timing.CurTime - chip.PlacedAt).TotalSeconds / 0.35), 0f, 1f);
        var scale = 0.7f + EaseOutBack(age) * 0.3f;
        var radius = 0.075f * scale;
        var color = ChipColors[chip.PlayerSlot % ChipColors.Length];

        handle.DrawCircle(chip.Position + new Vector2(0.025f, -0.025f), radius, Color.Black.WithAlpha(0.55f), true);
        handle.DrawCircle(chip.Position, radius, color, true);
        handle.DrawCircle(chip.Position, radius * 0.64f, Color.White.WithAlpha(0.72f), false);
        handle.DrawLine(chip.Position + new Vector2(-radius * 0.55f, 0f),
            chip.Position + new Vector2(radius * 0.55f, 0f),
            Color.White.WithAlpha(0.85f));
    }

    private void DrawScreen(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } viewport)
            return;

        var mouse = _input.MouseScreenPosition;
        var mouseViewport = mouse.IsValid
            ? _ui.MouseGetControl(mouse) as IViewportControl
            : null;
        var mapPosition = ReferenceEquals(mouseViewport, viewport)
            ? mouseViewport.PixelToMap(mouse.Position)
            : default;
        RouletteRenderCell? hovered = null;
        var handle = args.ScreenHandle;

        foreach (var (uid, data) in _renderData)
        {
            if (!_entity.TryGetComponent(uid, out TransformComponent? transform) || transform.MapID != args.MapId)
                continue;

            var worldPosition = _transform.GetWorldPosition(transform);
            if (!args.WorldBounds.Enlarged(4f).Contains(worldPosition))
                continue;

            var worldMatrix = _transform.GetWorldMatrix(uid);
            for (var i = 0; i < data.Cells.Length; i++)
            {
                var cell = data.Cells[i];
                var world = Vector2.Transform(cell.Center, worldMatrix);
                var dimensions = handle.GetDimensions(_amountFont, cell.TotalText, 1f);
                var position = viewport.WorldToScreen(world) - dimensions / 2f + new Vector2(0f, -13f);
                var box = UIBox2.FromDimensions(position - new Vector2(3f, 1f), dimensions + new Vector2(6f, 2f));
                handle.DrawRect(box, Color.FromHex("#101410C8"));
                handle.DrawString(_amountFont, position, cell.TotalText, 1f, Color.White);
            }

            if (!ReferenceEquals(mouseViewport, viewport) || transform.MapID != mapPosition.MapId)
                continue;

            var local = Vector2.Transform(mapPosition.Position, _transform.GetInvWorldMatrix(uid));
            var bestDistance = float.MaxValue;
            for (var i = 0; i < data.Cells.Length; i++)
            {
                var cell = data.Cells[i];
                var offset = Vector2.Abs(local - cell.Center);
                if (offset.X > cell.HalfSize.X || offset.Y > cell.HalfSize.Y)
                    continue;

                var distance = Vector2.DistanceSquared(local, cell.Center);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                hovered = cell;
            }
        }

        if (hovered != null)
            DrawTooltipBox(handle, mouse.Position + new Vector2(18f, 18f), hovered.TooltipLines);
    }

    private void DrawTooltipBox(DrawingHandleScreen handle, Vector2 position, string[] lines)
    {
        var lineHeight = _tooltipFont.GetLineHeight(1f);
        var width = 0f;
        for (var i = 0; i < lines.Length; i++)
            width = MathF.Max(width, handle.GetDimensions(_tooltipFont, lines[i], 1f).X);

        var size = new Vector2(width + 16f, lineHeight * lines.Length + 12f);
        handle.DrawRect(UIBox2.FromDimensions(position, size), Color.FromHex("#101410E8"));
        handle.DrawRect(UIBox2.FromDimensions(position, size), Color.FromHex("#D9B44A"), false);
        for (var i = 0; i < lines.Length; i++)
            handle.DrawString(_tooltipFont, position + new Vector2(8f, 6f + lineHeight * i), lines[i]);
    }

    public void Update(EntityUid uid, RouletteWorldBet[] bets)
    {
        if (bets.Length == 0)
        {
            _renderData.Remove(uid);
            return;
        }

        var groups = new Dictionary<(RouletteBetType Type, int Number), List<RouletteWorldBet>>();
        for (var i = 0; i < bets.Length; i++)
        {
            var bet = bets[i];
            var number = bet.Type == RouletteBetType.Number ? bet.Number : -1;
            if (!groups.TryGetValue((bet.Type, number), out var group))
            {
                group = new List<RouletteWorldBet>();
                groups.Add((bet.Type, number), group);
            }

            group.Add(bet);
        }

        var chips = new List<RouletteRenderChip>(Math.Min(bets.Length, groups.Count * MaxVisibleChipsPerCell));
        var cells = new RouletteRenderCell[groups.Count];
        var cellIndex = 0;
        foreach (var ((type, number), group) in groups)
        {
            var center = GetCellCenter(type, number) + TableOffset;
            var total = 0;
            var tooltipEntries = Math.Min(group.Count, MaxTooltipEntries);
            var tooltipLines = new string[tooltipEntries + 1];
            for (var i = 0; i < group.Count; i++)
            {
                var bet = group[i];
                total += bet.Amount;
                if (i < tooltipEntries)
                {
                    tooltipLines[i + 1] = Loc.GetString("roulette-world-tooltip-entry",
                        ("player", bet.PlayerName),
                        ("amount", bet.Amount));
                }

                if (i >= MaxVisibleChipsPerCell || chips.Count >= MaxVisibleChipsPerTable)
                    continue;

                var visibleCount = Math.Min(group.Count, MaxVisibleChipsPerCell);
                var row = i / 3;
                var rowCount = Math.Min(3, visibleCount - row * 3);
                var offset = new Vector2((i % 3 - (rowCount - 1) / 2f) * 0.07f, row * 0.055f);
                chips.Add(new RouletteRenderChip(center + offset, bet.PlayerSlot, bet.PlacedAt));
            }

            tooltipLines[0] = Loc.GetString("roulette-world-tooltip-title",
                ("target", GetBetName(type, number)),
                ("amount", total));
            cells[cellIndex++] = new RouletteRenderCell(
                center,
                GetCellHalfSize(type, number),
                Loc.GetString("roulette-world-bet-total", ("amount", total)),
                tooltipLines);
        }

        _renderData[uid] = new RouletteRenderData(chips.ToArray(), cells);
    }

    public void Remove(EntityUid uid)
    {
        _renderData.Remove(uid);
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

    private static float EaseOutBack(float value)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var shifted = value - 1f;
        return 1f + c3 * shifted * shifted * shifted + c1 * shifted * shifted;
    }

    private sealed record RouletteRenderData(RouletteRenderChip[] Chips, RouletteRenderCell[] Cells);

    private sealed record RouletteRenderCell(
        Vector2 Center,
        Vector2 HalfSize,
        string TotalText,
        string[] TooltipLines);

    private readonly record struct RouletteRenderChip(Vector2 Position, byte PlayerSlot, TimeSpan PlacedAt);
}
