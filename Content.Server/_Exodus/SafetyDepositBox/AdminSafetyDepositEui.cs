using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.SafetyDepositBox;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace Content.Server._Exodus.SafetyDepositBox;

public sealed partial class AdminSafetyDepositEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private ILogManager _logs = default!;

    private readonly AdminSafetyDepositEuiState _state = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string? _initialQuery;
    private bool _closed;

    public AdminSafetyDepositEui(string? initialQuery = null)
    {
        IoCManager.InjectDependencies(this);
        _initialQuery = initialQuery;
    }

    private SafetyDepositBoxSystem Boxes => _entities.System<SafetyDepositBoxSystem>();
    private bool Authorized => !_closed && _admins.HasAdminFlag(Player, AdminFlags.Admin) &&
                               _players.TryGetSessionById(Player.UserId, out var session) && session == Player;

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
        if (!Authorized)
        {
            Close();
            return;
        }

        _adminLog.Add(LogType.Action, $"{Player:actor} opened safety deposit administration");
        Run(async () =>
        {
            await RefreshPlayersAsync();
            if (!string.IsNullOrWhiteSpace(_initialQuery) && Authorized)
                await SearchAsync(_initialQuery);
        });
    }

    public override void Closed()
    {
        _closed = true;
        _cancellation.Cancel();
        _admins.OnPermsChanged -= OnPermsChanged;
        base.Closed();
    }

    public override EuiStateBase GetNewState()
    {
        if (!Authorized)
            return new AdminSafetyDepositEuiState();

        _state.CanSpawn = _admins.HasAdminFlag(Player, AdminFlags.Spawn);
        return _state;
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
            return;

        if (!Authorized)
        {
            Close();
            return;
        }

        StateDirty();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (!Authorized || _state.Busy)
            return;

        switch (msg)
        {
            case AdminSafetyDepositRefreshMessage:
                Run(RefreshAsync);
                break;
            case AdminSafetyDepositSearchMessage search when search.Query is { Length: > 0 and <= 200 }:
                Run(() => SearchAsync(search.Query));
                break;
            case AdminSafetyDepositSelectPlayerMessage select:
                Run(() => SearchAsync(select.UserId.ToString()));
                break;
            case AdminSafetyDepositSelectBoxMessage select when _state.SelectedUser != null:
                Run(() => LoadBoxAsync(select.BoxId, true));
                break;
            case AdminSafetyDepositModifyMessage modify when _state.SelectedUser is { } owner:
                if (_state.SelectedBox != modify.BoxId || _state.ViewId != modify.ViewId || !_state.CanEdit)
                {
                    _state.Message = Loc.GetString("admin-safety-deposit-error-stale");
                    StateDirty();
                    return;
                }

                Run(async () =>
                {
                    _adminLog.Add(LogType.Action, LogImpact.High,
                        $"{Player:actor} requested safety deposit {modify.Action} for owner {new NetUserId(owner):subject}, box {modify.BoxId}, record {modify.RecordId}, entity {modify.Entity}");
                    var message = await Boxes.AdminModifyBoxAsync(Player, owner, modify, () => Authorized);
                    _adminLog.Add(LogType.Action, LogImpact.High,
                        $"{Player:actor} safety deposit {modify.Action} for owner {new NetUserId(owner):subject}, box {modify.BoxId} returned {message}");
                    if (!Authorized)
                        return;

                    _state.Message = Loc.GetString(message);
                    await RefreshAsync();
                });
                break;
        }
    }

    /// <summary>Serializes requests per window; the system additionally locks across windows and player consoles.</summary>
    private void Run(Func<Task> operation)
    {
        if (!Authorized || _state.Busy)
            return;

        _state.Busy = true;
        _state.Message = string.Empty;
        StateDirty();
        _ = RunAsync(operation);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_closed)
        {
        }
        catch (Exception ex)
        {
            _logs.GetSawmill("admin.safety-deposit").Error($"Safety deposit panel request by {Player.UserId} failed: {ex}");
            _state.Message = Loc.GetString("admin-safety-deposit-error-database");
            _state.CanEdit = false;
            _state.ViewId = Guid.NewGuid();
        }
        finally
        {
            _state.Busy = false;
            if (Authorized)
                StateDirty();
        }
    }

    private async Task RefreshAsync()
    {
        await RefreshPlayersAsync();
        if (!Authorized || _state.SelectedUser is not { } user)
            return;

        var selectedBox = _state.SelectedBox;
        await LoadPlayerBoxesAsync(user, _state.SelectedName);
        if (Authorized && selectedBox is { } box)
            await LoadBoxAsync(box, false);
    }

    private async Task RefreshPlayersAsync()
    {
        var online = new Dictionary<Guid, string>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected)
                online[session.UserId.UserId] = session.Name;
        }

        var boxes = await _db.GetAdminSafetyDepositBoxes(new List<Guid>(online.Keys), _cancellation.Token);
        if (!Authorized)
            return;

        var counts = new Dictionary<Guid, int>();
        foreach (var box in boxes)
            counts[box.OwnerUserId] = counts.GetValueOrDefault(box.OwnerUserId) + 1;

        _state.Players.Clear();
        foreach (var (id, name) in online)
        {
            if (_players.TryGetSessionById(new NetUserId(id), out var session) && session.Status != SessionStatus.Disconnected)
                _state.Players.Add(new AdminSafetyDepositPlayer(id, name, counts.GetValueOrDefault(id)));
        }

        _state.Players.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SearchAsync(string query)
    {
        var player = await _locator.LookupIdByNameOrIdAsync(query.Trim(), _cancellation.Token);
        if (!Authorized)
            return;

        if (player == null)
        {
            ClearSelection();
            await RefreshPlayersAsync();
            _state.Message = Loc.GetString("admin-safety-deposit-error-player");
            return;
        }

        _adminLog.Add(LogType.Action,
            $"{Player:actor} looked up safety deposit boxes of {player.UserId:subject} ({player.Username})");
        await LoadPlayerBoxesAsync(player.UserId.UserId, player.Username);
    }

    private async Task LoadPlayerBoxesAsync(Guid userId, string name)
    {
        var boxes = await _db.GetAdminSafetyDepositBoxes([userId], _cancellation.Token);
        if (!Authorized)
            return;

        ClearSelection();
        _state.SelectedUser = userId;
        _state.SelectedName = name;
        var physical = Boxes.GetAdminPhysicalBoxes();
        var round = _entities.System<GameTicker>().RoundId;
        foreach (var box in boxes)
        {
            var hasPhysical = physical.TryGetValue(box.BoxId, out var entity);
            var status = box.LastWithdrawn == null
                ? (hasPhysical ? AdminSafetyDepositBoxStatus.Unavailable : AdminSafetyDepositBoxStatus.Stored)
                : box.LastWithdrawnRoundId != round
                    ? AdminSafetyDepositBoxStatus.Lost
                    : hasPhysical ? AdminSafetyDepositBoxStatus.Withdrawn : AdminSafetyDepositBoxStatus.Unavailable;
            _state.Boxes.Add(new AdminSafetyDepositBox(box.BoxId, box.CharacterIndex, box.OwnerName,
                box.ProtoId, box.Nickname, status, box.ItemCount, hasPhysical ? _entities.GetNetEntity(entity) : null));
        }

        // Keep the selected offline account visible alongside connected accounts.
        var listed = false;
        for (var i = _state.Players.Count - 1; i >= 0; i--)
        {
            var player = _state.Players[i];
            if (player.UserId == userId)
            {
                _state.Players[i] = new AdminSafetyDepositPlayer(userId, name, boxes.Count);
                listed = true;
            }
            else if (!_players.TryGetSessionById(new NetUserId(player.UserId), out _))
            {
                _state.Players.RemoveAt(i);
            }
        }

        if (!listed)
            _state.Players.Add(new AdminSafetyDepositPlayer(userId, name, boxes.Count));
    }

    private async Task LoadBoxAsync(Guid boxId, bool auditView)
    {
        var box = await _db.GetSafetyDepositBox(boxId, _cancellation.Token);
        if (!Authorized)
            return;

        if (box == null || box.OwnerUserId != _state.SelectedUser)
        {
            ClearBox();
            _state.Message = Loc.GetString("admin-safety-deposit-error-box");
            return;
        }

        if (auditView)
        {
            await _db.AddSafetyDepositAdminAudit(new SafetyDepositAdminAudit
            {
                Id = Guid.NewGuid(),
                AdminUserId = Player.UserId.UserId,
                AdminName = Player.Name,
                OwnerUserId = box.OwnerUserId,
                CharacterIndex = box.CharacterIndex,
                BoxId = box.BoxId,
                CreatedAt = DateTime.UtcNow,
                Action = "View",
                Result = "success",
                Details = "Viewed safety deposit contents.",
                RoundId = _entities.System<GameTicker>().RoundId,
            }, _cancellation.Token);
            if (!Authorized)
                return;

            _adminLog.Add(LogType.Action,
                $"{Player:actor} viewed safety deposit box {box.BoxId}, owner {new NetUserId(box.OwnerUserId):subject}, character slot {box.CharacterIndex}");
        }

        var history = await _db.GetSafetyDepositAdminAudits(boxId, cancel: _cancellation.Token);
        if (!Authorized)
            return;

        // A fresh read after the audit awaits avoids showing a consumed snapshot alongside current world entities.
        box = await _db.GetSafetyDepositBox(boxId, _cancellation.Token);
        if (!Authorized)
            return;
        if (box == null || box.OwnerUserId != _state.SelectedUser)
        {
            ClearBox();
            return;
        }

        var physical = Boxes.GetAdminPhysicalBoxes();
        var hasPhysical = physical.TryGetValue(boxId, out var entity);
        var round = _entities.System<GameTicker>().RoundId;
        var status = box.LastWithdrawn == null
            ? (hasPhysical ? AdminSafetyDepositBoxStatus.Unavailable : AdminSafetyDepositBoxStatus.Stored)
            : box.LastWithdrawnRoundId != round
                ? AdminSafetyDepositBoxStatus.Lost
                : hasPhysical ? AdminSafetyDepositBoxStatus.Withdrawn : AdminSafetyDepositBoxStatus.Unavailable;
        for (var i = 0; i < _state.Boxes.Count; i++)
        {
            if (_state.Boxes[i].BoxId != boxId)
                continue;

            _state.Boxes[i] = new AdminSafetyDepositBox(boxId, box.CharacterIndex, box.OwnerName,
                box.ProtoId, box.Nickname, status, box.Items.Count, hasPhysical ? _entities.GetNetEntity(entity) : null);
            break;
        }

        _state.SelectedBox = boxId;
        _state.ViewId = Guid.NewGuid();
        _state.Items = Boxes.GetAdminBoxItems(box, hasPhysical ? entity : null);
        _state.CanEdit = !Boxes.IsAdminBoxBusy(boxId) && !(box.LastWithdrawn == null && hasPhysical);
        _state.Audit.Clear();
        foreach (var entry in history)
        {
            _state.Audit.Add(new AdminSafetyDepositAuditEntry(entry.CreatedAt, entry.AdminName,
                LocalizeAction(entry.Action), LocalizeResult(entry.Result), entry.Details));
        }
    }

    private void ClearSelection()
    {
        _state.SelectedUser = null;
        _state.SelectedName = string.Empty;
        _state.Boxes.Clear();
        ClearBox();
    }

    private void ClearBox()
    {
        _state.SelectedBox = null;
        _state.ViewId = Guid.NewGuid();
        _state.Items.Clear();
        _state.Audit.Clear();
        _state.CanEdit = false;
    }

    private static string LocalizeAction(string action) => Loc.GetString(action switch
    {
        "View" => "admin-safety-deposit-action-view",
        "Add" => "admin-safety-deposit-action-add",
        "AddFromHand" => "admin-safety-deposit-action-add-from-hand",
        "Withdraw" => "admin-safety-deposit-action-withdraw",
        "Delete" => "admin-safety-deposit-action-delete",
        "Rollback" => "admin-safety-deposit-action-rollback",
        _ => "admin-safety-deposit-action-unknown",
    });

    private static string LocalizeResult(string result) => Loc.GetString(result switch
    {
        "success" => "admin-safety-deposit-result-success",
        "failed" => "admin-safety-deposit-result-failed",
        "rolled-back" => "admin-safety-deposit-result-rolled-back",
        _ => "admin-safety-deposit-result-pending",
    });
}
