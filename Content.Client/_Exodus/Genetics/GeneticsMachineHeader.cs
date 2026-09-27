using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticsMachineHeader : PanelContainer
{
    private readonly GeneticProcedureAnimation _animation = new();
    private readonly Label _status = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _mutagen = new() { FontColorOverride = GeneticsUiTheme.Muted };

    public GeneticsMachineHeader(string module)
    {
        PanelOverride = GeneticsUiTheme.Box(GeneticsUiTheme.Surface, GeneticsUiTheme.Border, 10);
        var row = GeneticsUiTheme.Row(12);
        row.AddChild(_animation);
        var text = GeneticsUiTheme.Column(2);
        text.HorizontalExpand = true;
        text.AddChild(new Label
        {
            Text = Loc.GetString("genetics-hive-brand"),
            FontColorOverride = GeneticsUiTheme.Accent,
            StyleClasses = { "LabelHeading" },
        });
        text.AddChild(GeneticsUiTheme.Caption(module));
        var status = GeneticsUiTheme.Row();
        status.AddChild(_status);
        status.AddChild(_mutagen);
        text.AddChild(status);
        row.AddChild(text);
        AddChild(row);
    }

    public void UpdateState(bool powered, bool busy, float mutagen)
    {
        _animation.Powered = powered;
        _animation.Busy = powered && busy;
        _status.Text = Loc.GetString(!powered ? "genetics-offline" : busy ? "genetics-busy" : "genetics-ready");
        _status.FontColorOverride = powered && !busy ? GeneticsUiTheme.Accent : GeneticsUiTheme.Warning;
        _mutagen.Text = Loc.GetString("genetics-ui-mutagen", ("amount", mutagen));
    }
}

/// <summary>Pixel-like DNA instrument graphic. Animation follows the server's busy state, not a made-up progress timer.</summary>
public sealed class GeneticProcedureAnimation : Control
{
    public bool Busy;
    public bool Powered = true;
    private TimeSpan _elapsed;

    public GeneticProcedureAnimation()
    {
        MinSize = new Vector2(60, 60);
        MaxSize = new Vector2(60, 60);
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (Busy && VisibleInTree)
            _elapsed += TimeSpan.FromSeconds(args.DeltaSeconds);
        else
            _elapsed = TimeSpan.Zero;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var phase = (float) (_elapsed.TotalSeconds % 2) * MathF.PI;
        var bright = Powered ? GeneticsUiTheme.Accent : GeneticsUiTheme.Border;
        var faint = Powered ? Color.FromHex("#775196") : GeneticsUiTheme.Border;
        PixelRect(handle, 0, 0, 60, 60, GeneticsUiTheme.Background);
        for (var row = 0; row < 10; row++)
        {
            var wave = MathF.Sin(row * 0.7f + phase);
            var left = 28 + MathF.Round(wave * 17 / 2) * 2;
            var right = 56 - left;
            var y = 5 + row * 5;
            PixelRect(handle, MathF.Min(left, right), y + 1, MathF.Abs(left - right) + 3, 1, GeneticsUiTheme.Border);
            PixelRect(handle, left, y, 4, 3, wave > 0 ? bright : faint);
            PixelRect(handle, right, y, 4, 3, wave > 0 ? faint : bright);
        }
        if (Busy)
        {
            var scan = 4 + (float) (_elapsed.TotalSeconds % 1.5 / 1.5) * 50;
            PixelRect(handle, 2, scan, 56, 2, GeneticsUiTheme.Warning.WithAlpha(0.65f));
        }
        PixelRect(handle, 0, 0, 8, 1, bright);
        PixelRect(handle, 0, 0, 1, 8, bright);
        PixelRect(handle, 52, 59, 8, 1, bright);
        PixelRect(handle, 59, 52, 1, 8, bright);
    }

    private void PixelRect(DrawingHandleScreen handle, float x, float y, float width, float height, Color color)
    {
        handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y) * UIScale, new Vector2(width, height) * UIScale), color);
    }
}
