using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticPrinterWindow : FancyWindow
{
    public event Action<GeneticPrinterMessage>? OperationRequested;
    private readonly GeneticsMachineHeader _header = new("genetics-ui-printer-module");
    private readonly GeneticDiskContentsControl _contents = new();
    private readonly Button _block;
    private readonly Button _genome;
    private readonly ConfirmButton _reset;
    private readonly ConfirmButton _clear;
    private GeneticPrinterUiState? _state;

    public GeneticPrinterWindow()
    {
        Title = Loc.GetString("genetics-printer-title");
        Stylesheet = GeneticsUiTheme.CreateStylesheet(UserInterfaceManager.Stylesheet);
        MinSize = new Vector2(600, 640);
        SetSize = new Vector2(680, 680);
        var scroll = new ScrollContainer { HScrollEnabled = false };
        ContentsContainer.AddChild(scroll);
        var root = GeneticsUiTheme.Column(8);
        root.Margin = new Thickness(10);
        scroll.AddChild(root);
        root.AddChild(_header);
        var map = GeneticsUiTheme.Column();
        var caption = GeneticsUiTheme.Caption("genetics-ui-disk-map");
        caption.ToolTip = Loc.GetString("genetics-printer-help");
        map.AddChild(caption);
        map.AddChild(_contents);
        var diskPanel = GeneticsUiTheme.Panel(map);
        diskPanel.MinHeight = 320;
        root.AddChild(diskPanel);

        var output = GeneticsUiTheme.Column();
        output.AddChild(GeneticsUiTheme.Caption("genetics-ui-print-section"));
        var printing = GeneticsUiTheme.Row();
        _block = GeneticsUiTheme.StyleButton(new Button { Text = Loc.GetString("genetics-print"), Disabled = true });
        _block.OnPressed += _ => Send(GeneticPrinterOperation.PrintBlock);
        printing.AddChild(_block);
        _genome = GeneticsUiTheme.StyleButton(new Button { Text = Loc.GetString("genetics-print-genome"), Disabled = true });
        _genome.ToolTip = Loc.GetString("genetics-ui-full-genome-hint");
        _genome.OnPressed += _ => Send(GeneticPrinterOperation.PrintGenome);
        printing.AddChild(_genome);
        output.AddChild(printing);
        root.AddChild(GeneticsUiTheme.Panel(output));

        var editing = GeneticsUiTheme.Column();
        var editCaption = GeneticsUiTheme.Caption("genetics-ui-disk-edit-section");
        editCaption.ToolTip = Loc.GetString("genetics-ui-disk-edit-hint");
        editing.AddChild(editCaption);
        var buttons = GeneticsUiTheme.Row();
        _reset = GeneticsUiTheme.StyleButton(new ConfirmButton
        {
            Text = Loc.GetString("genetics-ui-reset-block"),
            ConfirmationText = Loc.GetString("genetics-ui-confirm-reset"),
            Disabled = true,
            StyleClasses = { GeneticsUiTheme.DangerClass },
        });
        _reset.OnPressed += _ => Send(GeneticPrinterOperation.ResetBlock);
        buttons.AddChild(_reset);
        _clear = GeneticsUiTheme.StyleButton(new ConfirmButton
        {
            Text = Loc.GetString("genetics-ui-clear-disk"),
            ConfirmationText = Loc.GetString("genetics-ui-confirm-clear"),
            Disabled = true,
            StyleClasses = { GeneticsUiTheme.DangerClass },
        });
        _clear.OnPressed += _ => Send(GeneticPrinterOperation.ClearDisk);
        buttons.AddChild(_clear);
        editing.AddChild(buttons);
        root.AddChild(GeneticsUiTheme.Panel(editing));
        _contents.SelectionChanged += () =>
        {
            CancelConfirmations();
            UpdateButtons();
        };
    }

    public void UpdateState(GeneticPrinterUiState state)
    {
        // A confirmation always belongs to one disk revision, not a subsequently inserted sample.
        if (_state?.Disk.Disk != state.Disk.Disk || _state?.Disk.Revision != state.Disk.Revision ||
            _state?.Busy != state.Busy || _state?.Powered != state.Powered)
            CancelConfirmations();
        _state = state;
        _header.UpdateState(state.Powered, state.Busy, state.Mutagen);
        _contents.UpdateData(state.Disk);
        UpdateButtons();
    }

    private void CancelConfirmations()
    {
        _reset.IsConfirming = false;
        _clear.IsConfirming = false;
        _reset.Disabled = true;
        _clear.Disabled = true;
        _reset.Text = Loc.GetString("genetics-ui-reset-block");
        _clear.Text = Loc.GetString("genetics-ui-clear-disk");
    }

    private void UpdateButtons()
    {
        if (_state == null)
            return;
        var ready = _state.Powered && !_state.Busy && _state.Disk.Status == GeneticDiskStatus.Ready;
        _block.Disabled = !ready;
        _block.ToolTip = Loc.GetString("genetics-printer-print-block", ("number", _contents.SelectedBlock + 1));
        _genome.Disabled = !ready;
        // Keep ConfirmButton's own short anti-double-click delay intact during unrelated state refreshes.
        if (!_reset.IsConfirming)
            _reset.Disabled = !ready;
        _reset.ToolTip = Loc.GetString("genetics-printer-reset-block", ("number", _contents.SelectedBlock + 1));
        if (!_clear.IsConfirming)
            _clear.Disabled = !_state.Powered || _state.Busy ||
                _state.Disk.Status is GeneticDiskStatus.Missing or GeneticDiskStatus.Empty;
    }

    private void Send(GeneticPrinterOperation operation)
    {
        if (_state is not { Powered: true, Busy: false } state || state.Disk.Disk is not { } disk)
            return;
        OperationRequested?.Invoke(new GeneticPrinterMessage(operation, disk, state.Disk.Revision, _contents.SelectedBlock));
    }
}
