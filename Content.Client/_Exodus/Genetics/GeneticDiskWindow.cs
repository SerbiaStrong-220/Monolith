using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticDiskWindow : FancyWindow
{
    private readonly GeneticDiskContentsControl _contents = new();

    public GeneticDiskWindow()
    {
        Title = Loc.GetString("genetics-disk-title");
        Stylesheet = GeneticsUiTheme.CreateStylesheet(UserInterfaceManager.Stylesheet);
        MinSize = new Vector2(600, 480);
        SetSize = new Vector2(660, 500);
        var root = GeneticsUiTheme.Column(8);
        root.Margin = new Thickness(10);
        root.AddChild(new Label { Text = Loc.GetString("genetics-hive-brand"), FontColorOverride = GeneticsUiTheme.Accent });
        var help = new RichTextLabel();
        help.SetMessage(Loc.GetString("genetics-disk-help"));
        root.AddChild(help);
        root.AddChild(GeneticsUiTheme.Panel(_contents));
        ContentsContainer.AddChild(root);
    }

    public void UpdateState(GeneticDiskUiState state) => _contents.UpdateData(state.Disk);
}
