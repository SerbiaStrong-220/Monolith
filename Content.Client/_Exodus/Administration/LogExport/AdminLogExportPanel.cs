using System.Globalization;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Administration.LogExport;

/// <summary>
/// An inline confirmation panel that stays with the log controls when they move into a pop-out window.
/// </summary>
public sealed class AdminLogExportPanel : BoxContainer
{
    private readonly Button _begin = new() { Text = Loc.GetString("admin-logs-export-button"), Disabled = true };
    private readonly Button _cancel = new() { Text = Loc.GetString("admin-logs-export-cancel"), Visible = false };
    private readonly Button _openFolder = new() { Text = Loc.GetString("admin-logs-export-open-folder"), Visible = false };
    private readonly Label _status = new() { HorizontalExpand = true, ClipText = true, MouseFilter = MouseFilterMode.Stop };
    private readonly BoxContainer _confirmation = new() { Orientation = LayoutOrientation.Vertical, Visible = false, SeparationOverride = 4 };
    private readonly RichTextLabel _prompt = new();
    private readonly RichTextLabel _scope = new();
    private readonly Button _confirm = new() { Text = Loc.GetString("admin-logs-export-confirm") };
    private AdminLogExportClient? _client;
    private Func<int>? _selectedRound;

    public AdminLogExportPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        var actions = new BoxContainer { SeparationOverride = 6 };
        actions.AddChild(_begin);
        actions.AddChild(_cancel);
        actions.AddChild(_openFolder);
        actions.AddChild(_status);
        AddChild(actions);
        _confirmation.AddChild(_prompt);
        _confirmation.AddChild(_scope);
        _confirmation.AddChild(_confirm);
        AddChild(_confirmation);
        _begin.OnPressed += _ =>
        {
            if (_selectedRound != null)
                _client?.Begin(_selectedRound());
        };
        _cancel.OnPressed += args =>
        {
            if (_client != null)
                _ = _client.CancelAsync();
        };
        _confirm.OnPressed += _ => _client?.Confirm();
        _openFolder.OnPressed += _ => _client?.OpenFolder();
    }

    public void Bind(AdminLogExportClient client, Func<int> selectedRound)
    {
        Unbind();
        _client = client;
        _selectedRound = selectedRound;
        client.Changed += Update;
        Update();
    }

    public void Unbind()
    {
        if (_client != null)
            _client.Changed -= Update;
        _client = null;
        _selectedRound = null;
    }

    private void Update()
    {
        if (_client is not { } client)
            return;

        _begin.Disabled = !client.CanBegin;
        _cancel.Visible = client.Busy;
        _openFolder.Visible = client.CanOpenFolder;
        _confirmation.Visible = client.CanConfirm;
        _confirm.Disabled = !client.CanConfirm;
        _status.Visible = client.StatusKey != "admin-logs-export-ready";
        var status = Loc.TryGetString(client.StatusKey, out var localized)
            ? localized
            : Loc.GetString("admin-logs-export-error-read");
        _status.Text = status;
        if (client.StatusKey == "admin-logs-export-saved" && client.SavedPath is { } path)
            _status.Text = Loc.GetString("admin-logs-export-saved-file", ("file", path.Filename));
        if (client.Busy)
        {
            _status.Text = client.BytesReceived > 0 || client.TotalLogs > 0
                ? Loc.GetString("admin-logs-export-progress", ("status", status), ("round", client.RoundId),
                    ("mib", (client.BytesReceived / (1024d * 1024)).ToString("0.00", CultureInfo.InvariantCulture)), ("logs", client.TotalLogs))
                : Loc.GetString("admin-logs-export-round-status", ("status", status), ("round", client.RoundId));
        }
        _status.ToolTip = _status.Text;
        if (!client.CanConfirm)
            return;

        _prompt.SetMessage(Loc.GetString("admin-logs-export-confirm-" + client.ConfirmationStage));
        _scope.SetMessage(Loc.GetString("admin-logs-export-confirm-scope", ("round", client.RoundId)));
    }
}
