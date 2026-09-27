using System.Globalization;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticsWindow : FancyWindow
{
    public event Action<GeneticsMessage>? OperationRequested;
    private readonly GeneticsMachineHeader _header = new("genetics-ui-laboratory-module");
    private readonly Label _patient = new() { ClipText = true, HorizontalExpand = true, FontColorOverride = GeneticsUiTheme.Text };
    private readonly Label _selectedLabel = new() { MinWidth = 90, FontColorOverride = GeneticsUiTheme.Accent };
    private readonly Label _diskStatus = new() { HorizontalExpand = true, FontColorOverride = GeneticsUiTheme.Muted };
    private readonly RichTextLabel _adminDetails = new();
    private readonly GeneticBlockGrid _blocks = new();
    private readonly Label _empty = GeneticsUiTheme.Caption("genetics-ui-scan-required");
    private readonly BoxContainer _adminEdit = GeneticsUiTheme.Row();
    private readonly LineEdit _hex = new() { Text = "000", MinWidth = 90 };
    private readonly Button _help;
    private readonly Button _ejectPatient;
    private readonly Button _ejectDisk;
    private readonly List<Button> _patientButtons = new();
    private readonly List<Button> _scannedButtons = new();
    private readonly List<Button> _diskButtons = new();
    private readonly List<Button> _adminButtons = new();
    private readonly List<Control> _adminRows = new();
    private readonly List<(Button Button, int Buffer)> _bufferButtons = new();
    private readonly List<Button> _digits = new();
    private GeneticsUiState? _state;
    private int _selected;

    public GeneticsWindow()
    {
        Title = Loc.GetString("genetics-title");
        Stylesheet = GeneticsUiTheme.CreateStylesheet(UserInterfaceManager.Stylesheet);
        MinSize = new Vector2(600, 720);
        SetSize = new Vector2(680, 720);
        // Normal controls fit without scrolling; the scroll also accommodates the extra ADMIN buffers.
        var scroll = new ScrollContainer { HScrollEnabled = false };
        ContentsContainer.AddChild(scroll);
        var root = GeneticsUiTheme.Column();
        root.Margin = new Thickness(10);
        scroll.AddChild(root);
        root.AddChild(_header);

        var patientRow = GeneticsUiTheme.Row();
        patientRow.AddChild(_patient);
        var scan = AddButton(patientRow, "genetics-scan", GeneticsOperation.Scan, false);
        scan.HorizontalExpand = false;
        scan.MinWidth = 80;
        _ejectPatient = AddButton(patientRow, "genetics-eject-patient", GeneticsOperation.EjectPatient, false, requiresPatient: false);
        _ejectPatient.HorizontalExpand = false;
        _ejectPatient.MinWidth = 170;
        _help = GeneticsUiTheme.StyleButton(new Button { Text = "?", MinWidth = 32, HorizontalExpand = false });
        _help.HorizontalExpand = false;
        patientRow.AddChild(_help);
        root.AddChild(patientRow);

        var map = GeneticsUiTheme.Column(4);
        map.AddChild(GeneticsUiTheme.Caption("genetics-ui-block-map"));
        map.AddChild(_blocks);
        _empty.MinHeight = 232;
        _empty.Align = Label.AlignMode.Center;
        map.AddChild(_empty);
        root.AddChild(GeneticsUiTheme.Panel(map));

        var editing = GeneticsUiTheme.Column();
        var edit = GeneticsUiTheme.Row();
        edit.AddChild(_selectedLabel);
        for (var digit = 0; digit < 3; digit++)
        {
            var button = AddButton(edit, "genetics-edit", GeneticsOperation.Edit, digit: digit);
            button.MinSize = new Vector2(48, 40);
            button.HorizontalExpand = false;
            button.ToolTip = Loc.GetString("genetics-digit-tooltip", ("number", digit + 1));
            _digits.Add(button);
        }
        var hint = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(6, 0, 0, 0) };
        hint.SetMessage(Loc.GetString("genetics-ui-edit-hint"));
        edit.AddChild(hint);
        editing.AddChild(edit);
        editing.AddChild(_adminDetails);
        _adminEdit.AddChild(_hex);
        AddButton(_adminEdit, "genetics-set-block", GeneticsOperation.SetBlock);
        _adminButtons.Add(AddButton(_adminEdit, "genetics-reset", GeneticsOperation.Reset));
        editing.AddChild(_adminEdit);
        root.AddChild(GeneticsUiTheme.Panel(editing));

        var printing = GeneticsUiTheme.Row();
        AddButton(printing, "genetics-print", GeneticsOperation.PrintInjector);
        AddButton(printing, "genetics-print-genome", GeneticsOperation.PrintGenome);
        root.AddChild(printing);

        var disk = GeneticsUiTheme.Column(4);
        var diskTitle = GeneticsUiTheme.Row();
        diskTitle.AddChild(_diskStatus);
        var diskHelp = GeneticsUiTheme.Caption("genetics-ui-disk-hint");
        diskHelp.ToolTip = Loc.GetString("genetics-lab-disk-help");
        diskTitle.AddChild(diskHelp);
        disk.AddChild(diskTitle);
        var diskRow = GeneticsUiTheme.Row();
        var write = AddButton(diskRow, "genetics-write-disk", GeneticsOperation.WriteDisk);
        write.ToolTip = Loc.GetString("genetics-lab-disk-help");
        _diskButtons.Add(write);
        _ejectDisk = AddButton(diskRow, "genetics-eject-disk", GeneticsOperation.EjectDisk, false, requiresPatient: false);
        disk.AddChild(diskRow);
        root.AddChild(GeneticsUiTheme.Panel(disk));

        for (var i = 0; i < 3; i++)
        {
            var row = GeneticsUiTheme.Column(4);
            row.AddChild(GeneticsUiTheme.Caption("genetics-ui-admin-buffer"));
            var controls = GeneticsUiTheme.Row();
            controls.AddChild(new Label { Text = Loc.GetString("genetics-buffer", ("number", i + 1)) });
            AddButton(controls, "genetics-store", GeneticsOperation.StoreBuffer, buffer: i);
            var restore = AddButton(controls, "genetics-restore", GeneticsOperation.RestoreBuffer, buffer: i);
            _bufferButtons.Add((restore, i));
            _diskButtons.Add(AddButton(controls, "genetics-read-disk", GeneticsOperation.ReadDisk, buffer: i));
            row.AddChild(controls);
            var bufferPrinting = GeneticsUiTheme.Row();
            _bufferButtons.Add((AddButton(bufferPrinting, "genetics-print-buffer-block", GeneticsOperation.PrintBufferInjector, buffer: i), i));
            _bufferButtons.Add((AddButton(bufferPrinting, "genetics-print-buffer-genome", GeneticsOperation.PrintBufferGenome, buffer: i), i));
            row.AddChild(bufferPrinting);
            var panel = GeneticsUiTheme.Panel(row);
            panel.Visible = false;
            root.AddChild(panel);
            _adminRows.Add(panel);
        }

        _blocks.BlockSelected += index =>
        {
            if (_state == null || index >= _state.Blocks.Count)
                return;
            _selected = index;
            _hex.Text = _state.Blocks[index].Value.ToString("X3");
            UpdateSelection();
        };
        _adminDetails.Visible = false;
        _adminEdit.Visible = false;
    }

    private Button AddButton(BoxContainer parent, string label, GeneticsOperation operation, bool scanned = true,
        int buffer = 0, int digit = 0, bool requiresPatient = true)
    {
        var button = GeneticsUiTheme.StyleButton(new Button { Text = Loc.GetString(label), Disabled = true });
        button.ToolTip = button.Text;
        button.OnPressed += _ =>
        {
            if (_state == null)
                return;
            var value = 0;
            if (operation == GeneticsOperation.SetBlock &&
                (_hex.Text.Length is < 1 or > 3 ||
                 !int.TryParse(_hex.Text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)))
                return;
            OperationRequested?.Invoke(new GeneticsMessage(operation, _state.PatientEntity, _state.Revision, _selected, value, buffer, digit));
        };
        parent.AddChild(button);
        if (requiresPatient)
            _patientButtons.Add(button);
        if (scanned)
            _scannedButtons.Add(button);
        return button;
    }

    public void UpdateState(GeneticsUiState state)
    {
        if (_state?.PatientEntity != state.PatientEntity)
            _selected = 0;
        _state = state;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, state.Blocks.Count - 1));
        _header.UpdateState(state.Powered, state.Busy, state.Mutagen);
        _patient.Text = state.PatientEntity != null && !state.Living
            ? Loc.GetString("genetics-dead-patient") : state.Patient;
        _patient.ToolTip = state.Patient;
        _help.ToolTip = Loc.GetString(state.Debug ? "genetics-admin-instructions" : "genetics-instructions");
        _diskStatus.Text = Loc.GetString(state.Disk ? "genetics-ui-disk-inserted" : "genetics-disk-missing");
        var available = state.Powered && !state.Busy && state.PatientEntity != null && state.Living;
        foreach (var button in _patientButtons)
            button.Disabled = !available;
        foreach (var button in _scannedButtons)
            button.Disabled |= state.Revision < 0 || state.Blocks.Count == 0;
        foreach (var button in _diskButtons)
            button.Disabled |= !state.Disk;
        _ejectPatient.Disabled = state.PatientEntity == null;
        _ejectDisk.Disabled = !state.Disk;
        foreach (var (button, buffer) in _bufferButtons)
            button.Disabled |= buffer >= state.Buffers.Length || !state.Buffers[buffer];
        foreach (var button in _adminButtons)
            button.Visible = state.Debug;
        foreach (var row in _adminRows)
            row.Visible = state.Debug;
        _adminEdit.Visible = state.Debug;
        _adminDetails.Visible = state.Debug;
        _hex.Editable = available && state.Revision >= 0;

        _blocks.SetBlockCount(state.Blocks.Count);
        _blocks.Select(_selected);
        _empty.Visible = state.Blocks.Count == 0;
        _blocks.Visible = state.Blocks.Count != 0;
        for (var i = 0; i < state.Blocks.Count; i++)
        {
            var block = state.Blocks[i];
            _blocks.SetBlock(i, block.Value, state.Debug
                ? Loc.GetString("genetics-admin-block", ("number", i + 1), ("value", block.Value.ToString("X3")),
                    ("name", block.Name ?? Loc.GetString("genetics-empty-block")),
                    ("state", Loc.GetString(block.Active == true ? "genetics-active" : "genetics-inactive")))
                : null);
        }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_state == null)
            return;
        var block = _state.Blocks.Count > 0 ? _state.Blocks[_selected] : null;
        _selectedLabel.Text = block == null ? Loc.GetString("genetics-ui-no-block")
            : Loc.GetString("genetics-selected", ("number", _selected + 1));
        for (var digit = 0; digit < _digits.Count; digit++)
            _digits[digit].Text = block == null ? "—" : ((block.Value >> ((2 - digit) * 4)) & 0xF).ToString("X");
        if (_state.Debug)
        {
            _adminDetails.SetMessage(Loc.GetString("genetics-ui-admin-selection",
                ("name", block?.Name ?? Loc.GetString("genetics-empty-block")), ("stability", _state.Stability ?? 0)));
            _adminDetails.ToolTip = block?.Description;
        }
    }
}
