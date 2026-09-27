using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

/// <summary>Read-only genome map shared by the portable disk viewer and the injector printer.</summary>
public sealed class GeneticDiskContentsControl : BoxContainer
{
    public event Action? SelectionChanged;
    public int SelectedBlock => _blocks.SelectedBlock;
    private readonly RichTextLabel _status = new();
    private readonly GeneticBlockGrid _blocks = new();
    private readonly Label _selection = new() { FontColorOverride = GeneticsUiTheme.Accent };
    private GeneticDiskData? _data;

    public GeneticDiskContentsControl()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        AddChild(_status);
        AddChild(_blocks);
        AddChild(_selection);
        _blocks.BlockSelected += _ =>
        {
            UpdateSelection();
            SelectionChanged?.Invoke();
        };
    }

    public void UpdateData(GeneticDiskData data)
    {
        if (_data?.Disk != data.Disk)
            _blocks.Select(0);
        _data = data;
        var status = data.Status switch
        {
            GeneticDiskStatus.Missing => "genetics-disk-missing",
            GeneticDiskStatus.Empty => "genetics-disk-empty",
            GeneticDiskStatus.Ready => "genetics-disk-recorded",
            _ => "genetics-disk-incompatible",
        };
        _status.SetMessage(Loc.GetString(status, ("count", data.Blocks.Count)));
        _blocks.SetBlockCount(data.Blocks.Count);
        for (var i = 0; i < data.Blocks.Count; i++)
            _blocks.SetBlock(i, data.Blocks[i]);
        _blocks.Visible = data.Blocks.Count != 0;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _selection.Visible = _data is { Blocks.Count: > 0 };
        if (_data is { Blocks.Count: > 0 })
            _selection.Text = Loc.GetString("genetics-ui-selected-value", ("number", SelectedBlock + 1),
                ("value", _data.Blocks[SelectedBlock].ToString("X3")));
    }
}
