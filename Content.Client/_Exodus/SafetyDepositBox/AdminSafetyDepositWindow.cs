using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Eui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.SafetyDepositBox;

public sealed class AdminSafetyDepositWindow : FancyWindow
{
    private const int MaxSearchLength = 200;
    private const int MaxPrototypeLength = 200;
    private const int MaxReasonLength = 500;

    public event Action<EuiMessageBase>? Send;

    private readonly LineEdit _search = new()
    {
        HorizontalExpand = true,
        PlaceHolder = Loc.GetString("admin-safety-deposit-search-placeholder"),
    };
    private readonly Button _find = new() { Text = Loc.GetString("admin-safety-deposit-search") };
    private readonly Button _refresh = new() { Text = Loc.GetString("admin-safety-deposit-refresh") };
    private readonly ItemList _players = new() { VerticalExpand = true, HorizontalExpand = true };
    private readonly ItemList _boxes = new() { VerticalExpand = true, HorizontalExpand = true };
    private readonly ItemList _items = new() { VerticalExpand = true, HorizontalExpand = true };
    private readonly BoxContainer _audit = Vertical();
    private readonly Label _status = new() { ClipText = true };
    private readonly Label _selectedAccount = new() { ClipText = true };
    private readonly Label _selectedBox = new() { ClipText = true };
    private readonly Label _storageNotice = new() { ClipText = true };
    private readonly LineEdit _prototype = new()
    {
        HorizontalExpand = true,
        PlaceHolder = Loc.GetString("admin-safety-deposit-prototype-placeholder"),
    };
    private readonly LineEdit _reason = new()
    {
        HorizontalExpand = true,
        PlaceHolder = Loc.GetString("admin-safety-deposit-reason-placeholder"),
    };
    private readonly Button _add = new() { Text = Loc.GetString("admin-safety-deposit-add") };
    private readonly Button _addFromHand = new() { Text = Loc.GetString("admin-safety-deposit-add-from-hand") };
    private readonly Button _withdraw = new() { Text = Loc.GetString("admin-safety-deposit-withdraw") };
    private readonly Button _delete = new() { Text = Loc.GetString("admin-safety-deposit-delete") };
    private readonly Button _cancelDelete = new() { Text = Loc.GetString("admin-safety-deposit-cancel-delete"), Visible = false };
    private readonly Label _deleteWarning = new() { ClipText = true, Visible = false };
    private AdminSafetyDepositEuiState? _state;
    private AdminSafetyDepositItem? _selectedItem;
    private bool _updating;
    private bool _pending;
    private bool _confirmDelete;

    public AdminSafetyDepositWindow()
    {
        Title = Loc.GetString("admin-safety-deposit-title");
        MinSize = new Vector2(850, 560);
        SetSize = new Vector2(1100, 720);
        var root = Vertical();
        root.AddChild(Help("admin-safety-deposit-help"));
        var search = new BoxContainer { SeparationOverride = 6 };
        search.AddChild(_search);
        search.AddChild(_find);
        search.AddChild(_refresh);
        root.AddChild(search);
        root.AddChild(_status);
        root.AddChild(_selectedAccount);
        root.AddChild(_selectedBox);

        var tabs = new TabContainer { VerticalExpand = true };
        tabs.AddChild(BuildContents());
        tabs.SetTabTitle(0, Loc.GetString("admin-safety-deposit-tab-contents"));
        var history = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        history.AddChild(_audit);
        tabs.AddChild(history);
        tabs.SetTabTitle(1, Loc.GetString("admin-safety-deposit-tab-history"));
        root.AddChild(tabs);
        ContentsContainer.AddChild(root);

        _find.OnPressed += _ => Search();
        _search.OnTextEntered += _ => Search();
        _search.OnTextChanged += _ => UpdateActions();
        _refresh.OnPressed += _ => Request(new AdminSafetyDepositRefreshMessage());
        _players.OnItemSelected += OnPlayerSelected;
        _boxes.OnItemSelected += OnBoxSelected;
        _items.OnItemSelected += OnItemSelected;
        _items.OnItemDeselected += _ =>
        {
            if (_updating)
                return;

            _selectedItem = null;
            CancelDelete();
        };
        _prototype.OnTextChanged += _ => UpdateActions();
        _reason.OnTextChanged += _ => CancelDelete();
        _add.OnPressed += _ => Modify(AdminSafetyDepositAction.Add);
        _addFromHand.OnPressed += _ => Modify(AdminSafetyDepositAction.AddFromHand);
        _withdraw.OnPressed += _ => Modify(AdminSafetyDepositAction.Withdraw);
        _delete.OnPressed += _ =>
        {
            if (_delete.Disabled)
                return;

            if (_confirmDelete)
                Modify(AdminSafetyDepositAction.Delete);
            else
            {
                _confirmDelete = true;
                UpdateActions();
            }
        };
        _cancelDelete.OnPressed += _ => CancelDelete();
        UpdateActions();
    }

    private BoxContainer BuildContents()
    {
        var contents = Vertical();
        var lists = new BoxContainer { VerticalExpand = true, SeparationOverride = 8 };
        lists.AddChild(Column("admin-safety-deposit-players", _players));
        lists.AddChild(Column("admin-safety-deposit-boxes", _boxes));
        lists.AddChild(Column("admin-safety-deposit-items", _items));
        contents.AddChild(lists);
        contents.AddChild(_storageNotice);
        contents.AddChild(Help("admin-safety-deposit-stack-help"));
        var creation = new BoxContainer { SeparationOverride = 6 };
        creation.AddChild(_prototype);
        creation.AddChild(_add);
        creation.AddChild(_addFromHand);
        contents.AddChild(creation);
        contents.AddChild(_reason);
        var actions = new BoxContainer { SeparationOverride = 6 };
        actions.AddChild(_withdraw);
        actions.AddChild(_delete);
        actions.AddChild(_cancelDelete);
        contents.AddChild(actions);
        contents.AddChild(_deleteWarning);
        return contents;
    }

    public void UpdateState(AdminSafetyDepositEuiState state)
    {
        var previousItem = _state?.SelectedBox == state.SelectedBox ? _selectedItem : null;
        _updating = true;
        _pending = false;
        _confirmDelete = false;
        _state = state;
        _selectedItem = null;
        _status.Text = state.Busy ? Loc.GetString("admin-safety-deposit-busy") : state.Message;
        _status.ToolTip = _status.Text;
        _selectedAccount.Text = state.SelectedUser is { } user
            ? Loc.GetString("admin-safety-deposit-selected-account", ("name", state.SelectedName), ("id", user.ToString()))
            : Loc.GetString("admin-safety-deposit-no-account");
        _selectedAccount.ToolTip = _selectedAccount.Text;
        _selectedBox.Text = Loc.GetString("admin-safety-deposit-no-box");

        _players.Clear();
        foreach (var player in state.Players)
        {
            var row = _players.AddItem(Loc.GetString("admin-safety-deposit-player-row", ("name", player.Name), ("count", player.BoxCount)), metadata: player);
            row.TooltipText = Loc.GetString("admin-safety-deposit-selected-account", ("name", player.Name), ("id", player.UserId.ToString()));
            row.Selected = player.UserId == state.SelectedUser;
        }

        _boxes.Clear();
        foreach (var box in state.Boxes)
        {
            var status = BoxStatus(box.Status);
            var row = _boxes.AddItem(Loc.GetString("admin-safety-deposit-box-row", ("name", box.OwnerName),
                ("character", box.CharacterIndex + 1), ("status", status), ("id", box.BoxId.ToString())), metadata: box);
            row.TooltipText = Loc.GetString("admin-safety-deposit-box-details", ("name", box.OwnerName),
                ("character", box.CharacterIndex + 1), ("status", status), ("id", box.BoxId.ToString()),
                ("prototype", box.ProtoId), ("nickname", box.Nickname ?? Loc.GetString("admin-safety-deposit-no-nickname")),
                ("count", box.StoredItemCount));
            row.Selected = box.BoxId == state.SelectedBox;
            if (row.Selected)
                _selectedBox.Text = row.TooltipText;
        }

        _selectedBox.ToolTip = _selectedBox.Text;
        var selectedBox = SelectedBox();
        var worldContents = selectedBox?.Status == AdminSafetyDepositBoxStatus.Withdrawn;
        _storageNotice.Visible = selectedBox != null;
        _storageNotice.Text = Loc.GetString(worldContents ? "admin-safety-deposit-world-notice" : "admin-safety-deposit-stored-notice");
        _storageNotice.ToolTip = _storageNotice.Text;
        _items.Clear();
        foreach (var item in state.Items)
        {
            var source = Loc.GetString(item.RecordId == 0 ? "admin-safety-deposit-source-world"
                : selectedBox?.Status == AdminSafetyDepositBoxStatus.Stored ? "admin-safety-deposit-source-stored"
                : "admin-safety-deposit-source-recovery");
            var row = _items.AddItem(Loc.GetString("admin-safety-deposit-item-row", ("name", item.Name),
                ("count", item.Count), ("source", source)), metadata: item);
            row.TooltipText = Loc.GetString("admin-safety-deposit-item-details", ("name", item.Name),
                ("count", item.Count), ("source", source), ("prototype", item.ProtoId), ("record", item.RecordId));
            if (previousItem is { } previous && previous.RecordId == item.RecordId && previous.Entity == item.Entity)
            {
                _selectedItem = item;
                row.Selected = true;
            }
        }

        UpdateAudit(state);
        _updating = false;
        UpdateActions();
    }

    private void UpdateAudit(AdminSafetyDepositEuiState state)
    {
        _audit.DisposeAllChildren();
        _audit.AddChild(Help("admin-safety-deposit-history-help"));
        if (state.Audit.Count == 0)
            _audit.AddChild(new Label { Text = Loc.GetString("admin-safety-deposit-history-empty") });

        foreach (var entry in state.Audit)
        {
            var text = Loc.GetString("admin-safety-deposit-audit-row", ("date", entry.Date.ToString("u")),
                ("admin", entry.Admin), ("action", entry.Action), ("result", entry.Result), ("details", entry.Details));
            _audit.AddChild(new Label { Text = text, ToolTip = text, ClipText = true });
        }
    }

    private void OnPlayerSelected(ItemList.ItemListSelectedEventArgs args)
    {
        if (_updating || IsBusy() || args.ItemList[args.ItemIndex].Metadata is not AdminSafetyDepositPlayer player)
            return;

        _selectedItem = null;
        Request(new AdminSafetyDepositSelectPlayerMessage(player.UserId));
    }

    private void OnBoxSelected(ItemList.ItemListSelectedEventArgs args)
    {
        if (_updating || IsBusy() || args.ItemList[args.ItemIndex].Metadata is not AdminSafetyDepositBox box)
            return;

        _selectedItem = null;
        Request(new AdminSafetyDepositSelectBoxMessage(box.BoxId));
    }

    private void OnItemSelected(ItemList.ItemListSelectedEventArgs args)
    {
        if (_updating)
            return;

        _selectedItem = args.ItemList[args.ItemIndex].Metadata is AdminSafetyDepositItem item ? item : null;
        CancelDelete();
    }

    private void Search()
    {
        if (IsBusy() || string.IsNullOrWhiteSpace(_search.Text) || _search.Text.Length > MaxSearchLength)
            return;

        Request(new AdminSafetyDepositSearchMessage(_search.Text.Trim()));
    }

    private void Request(EuiMessageBase message)
    {
        if (IsBusy())
            return;

        _pending = true;
        CancelDelete();
        _status.Text = Loc.GetString("admin-safety-deposit-busy");
        Send?.Invoke(message);
    }

    private void Modify(AdminSafetyDepositAction action)
    {
        UpdateActions();
        if (_state is not { CanEdit: true, SelectedBox: { } boxId } || IsBusy() ||
            _reason.Text.Length > MaxReasonLength || SelectedBox() is not { } box)
            return;

        if (action is AdminSafetyDepositAction.Add or AdminSafetyDepositAction.AddFromHand)
        {
            var fromHand = action == AdminSafetyDepositAction.AddFromHand;
            if (fromHand ? _addFromHand.Disabled : _add.Disabled)
                return;

            var prototypeId = fromHand ? string.Empty : _prototype.Text.Trim();
            Request(new AdminSafetyDepositModifyMessage(boxId, _state.ViewId, action, 0, null, prototypeId, _reason.Text.Trim()));
            return;
        }

        if (_selectedItem is not { } item || _withdraw.Disabled || action == AdminSafetyDepositAction.Delete && !_confirmDelete)
            return;

        Request(new AdminSafetyDepositModifyMessage(box.BoxId, _state.ViewId, action, item.RecordId, item.Entity, string.Empty, _reason.Text.Trim()));
    }

    private void CancelDelete()
    {
        _confirmDelete = false;
        UpdateActions();
    }

    private void UpdateActions()
    {
        var busy = IsBusy();
        var box = SelectedBox();
        var canEdit = !busy && _state?.CanEdit == true && box != null;
        var validReason = _reason.Text.Length <= MaxReasonLength;
        var canAdd = canEdit && (box?.Status == AdminSafetyDepositBoxStatus.Stored ||
            box is { Status: AdminSafetyDepositBoxStatus.Withdrawn, Entity: not null });
        var canCreatePrototype = canAdd && _state?.CanSpawn == true;
        var canModifyItem = canEdit && validReason && _selectedItem is { } item && (item.RecordId > 0 || item.Entity != null);
        _refresh.Disabled = busy;
        _find.Disabled = busy || string.IsNullOrWhiteSpace(_search.Text) || _search.Text.Length > MaxSearchLength;
        _search.Editable = !busy;
        _prototype.Editable = canCreatePrototype;
        _reason.Editable = canEdit;
        _add.Disabled = !canCreatePrototype || !validReason || string.IsNullOrWhiteSpace(_prototype.Text) || _prototype.Text.Length > MaxPrototypeLength;
        _addFromHand.Disabled = !canAdd || !validReason;
        _withdraw.Disabled = !canModifyItem;
        _delete.Disabled = !canModifyItem;
        _delete.Text = Loc.GetString(_confirmDelete ? "admin-safety-deposit-confirm-delete" : "admin-safety-deposit-delete");
        _cancelDelete.Visible = _confirmDelete;
        _cancelDelete.Disabled = busy;
        _deleteWarning.Visible = _confirmDelete;
        _deleteWarning.Text = _selectedItem is { } selected
            ? Loc.GetString("admin-safety-deposit-delete-warning", ("name", selected.Name), ("count", selected.Count))
            : string.Empty;
        _deleteWarning.ToolTip = _deleteWarning.Text;
    }

    private bool IsBusy() => _pending || _state?.Busy == true;

    private AdminSafetyDepositBox? SelectedBox()
    {
        if (_state?.SelectedBox is not { } selected)
            return null;

        foreach (var box in _state.Boxes)
        {
            if (box.BoxId == selected)
                return box;
        }

        return null;
    }

    private static string BoxStatus(AdminSafetyDepositBoxStatus status) => Loc.GetString(status switch
    {
        AdminSafetyDepositBoxStatus.Stored => "admin-safety-deposit-status-stored",
        AdminSafetyDepositBoxStatus.Withdrawn => "admin-safety-deposit-status-withdrawn",
        AdminSafetyDepositBoxStatus.Lost => "admin-safety-deposit-status-lost",
        _ => "admin-safety-deposit-status-unavailable",
    });

    private static BoxContainer Vertical() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };

    private static BoxContainer Column(string title, Control list)
    {
        var column = Vertical();
        column.HorizontalExpand = true;
        column.AddChild(new Label { Text = Loc.GetString(title), ClipText = true });
        column.AddChild(list);
        return column;
    }

    private static RichTextLabel Help(string key)
    {
        var label = new RichTextLabel();
        label.SetMessage(Loc.GetString(key));
        return label;
    }
}
