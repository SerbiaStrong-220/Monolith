using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

/// <summary>Fixed ten-column genome map. Cells never encode whether a mutation is present or active.</summary>
public sealed class GeneticBlockGrid : GridContainer
{
    public event Action<int>? BlockSelected;
    public int SelectedBlock { get; private set; }
    private readonly List<(ContainerButton Button, Label Value)> _cells = new();

    public GeneticBlockGrid()
    {
        Columns = 10;
        HSeparationOverride = 4;
        VSeparationOverride = 4;
        HorizontalExpand = true;
    }

    public void SetBlockCount(int count)
    {
        if (_cells.Count == count)
            return;
        DisposeAllChildren();
        _cells.Clear();
        SelectedBlock = Math.Clamp(SelectedBlock, 0, Math.Max(0, count - 1));
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var button = GeneticsUiTheme.StyleButton(new ContainerButton
            {
                ToggleMode = true,
                MinSize = new Vector2(48, 44),
                StyleClasses = { GeneticsUiTheme.CellClass },
            });
            var labels = GeneticsUiTheme.Column(0);
            labels.MouseFilter = MouseFilterMode.Ignore;
            labels.AddChild(new Label
            {
                Text = (i + 1).ToString("D2"),
                Align = Label.AlignMode.Center,
                FontColorOverride = GeneticsUiTheme.Muted,
                StyleClasses = { "LabelSubText" },
                MouseFilter = MouseFilterMode.Ignore,
            });
            var value = new Label
            {
                Align = Label.AlignMode.Center,
                FontColorOverride = GeneticsUiTheme.Text,
                MouseFilter = MouseFilterMode.Ignore,
            };
            labels.AddChild(value);
            button.AddChild(labels);
            button.OnPressed += _ =>
            {
                Select(index);
                BlockSelected?.Invoke(index);
            };
            _cells.Add((button, value));
            AddChild(button);
        }
        Select(SelectedBlock);
    }

    public void SetBlock(int index, ushort value, string? tooltip = null)
    {
        var cell = _cells[index];
        cell.Value.Text = value.ToString("X3");
        cell.Button.ToolTip = tooltip ?? Loc.GetString("genetics-block", ("number", index + 1), ("value", value.ToString("X3")));
    }

    public void Select(int index)
    {
        SelectedBlock = Math.Clamp(index, 0, Math.Max(0, _cells.Count - 1));
        for (var i = 0; i < _cells.Count; i++)
            _cells[i].Button.Pressed = i == SelectedBlock;
    }
}
