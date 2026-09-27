using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticsAdminWindow : FancyWindow
{
    public event Action<GeneticsAdminSetBlockMessage>? BlockChanged;
    public event Action? RefreshRequested;
    private readonly RichTextLabel _status = new();
    private readonly BoxContainer _blocks = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };

    public GeneticsAdminWindow()
    {
        Title = Loc.GetString("genetics-admin-title");
        MinSize = new Vector2(520, 520);
        SetSize = new Vector2(600, 600);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        root.AddChild(_status);
        var help = new RichTextLabel();
        help.SetMessage(Loc.GetString("genetics-admin-help"));
        root.AddChild(help);
        var refresh = new Button { Text = Loc.GetString("genetics-admin-refresh") };
        refresh.OnPressed += _ => RefreshRequested?.Invoke();
        root.AddChild(refresh);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_blocks);
        root.AddChild(scroll);
        ContentsContainer.AddChild(root);
    }

    public void UpdateState(GeneticsAdminState state)
    {
        _status.SetMessage(Loc.GetString("genetics-admin-target", ("target", state.Target), ("stability", state.Stability)));
        _blocks.DisposeAllChildren();
        for (var i = 0; i < state.Blocks.Count; i++)
        {
            var index = i;
            var block = state.Blocks[i];
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 0, 0, 6) };
            var label = new RichTextLabel
            {
                HorizontalExpand = true,
                ToolTip = block.Description,
            };
            label.SetMessage(Loc.GetString("genetics-admin-block", ("number", i + 1), ("value", block.Value.ToString("X3")),
                ("name", block.Name ?? Loc.GetString("genetics-empty-block")),
                ("state", Loc.GetString(block.Active == true ? "genetics-active" : "genetics-inactive"))));
            row.AddChild(label);
            var buttons = new BoxContainer();
            var enable = new Button { Text = Loc.GetString("genetics-admin-enable"), Disabled = block.Active != false };
            var disable = new Button { Text = Loc.GetString("genetics-admin-disable"), Disabled = block.Active != true };
            enable.OnPressed += _ => BlockChanged?.Invoke(new GeneticsAdminSetBlockMessage(state.Context, state.Revision, index, true));
            disable.OnPressed += _ => BlockChanged?.Invoke(new GeneticsAdminSetBlockMessage(state.Context, state.Revision, index, false));
            buttons.AddChild(enable);
            buttons.AddChild(disable);
            row.AddChild(buttons);
            _blocks.AddChild(row);
        }
    }
}
