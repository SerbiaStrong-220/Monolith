using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Economy.Admin;

public sealed class MarketAdminEui(
    MarketAdminSystem system,
    MarketSettingsSystem settings,
    IAdminManager admins,
    IGameTiming timing) : BaseEui
{
    private readonly MarketAdminMutationChallenge _confirmation = new();
    private string _search = string.Empty;
    private ProtoId<MarketCommodityGroupPrototype>? _group;
    private int _page;
    private int _pageSize = 50;
    private string? _status;
    private bool _statusSuccess;
    private bool _closed;

    public override void Opened()
    {
        base.Opened();
        if (!system.CanAdminister(Player))
        {
            Close();
            return;
        }
        admins.OnPermsChanged += OnPermissionsChanged;
        settings.SettingsChanged += OnSettingsChanged;
        system.QuotesChanged += OnQuotesChanged;
        StateDirty();
    }

    public override void Closed()
    {
        _closed = true;
        _confirmation.Cancel();
        admins.OnPermsChanged -= OnPermissionsChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        system.QuotesChanged -= OnQuotesChanged;
        base.Closed();
    }

    public override EuiStateBase GetNewState()
    {
        if (_closed || IsShutDown)
            return new MarketAdminState();
        if (!system.CanAdminister(Player))
        {
            Close();
            return new MarketAdminState();
        }

        var state = system.GetState(Player, _search, _group, _page, _pageSize);
        _group = state.GroupFilter;
        _page = state.Page;
        state.Status = _status ?? settings.LastError;
        state.StatusSuccess = _statusSuccess;
        state.ConfirmationKind = _confirmation.Kind;
        state.ConfirmationStage = _confirmation.Stage;
        state.ConfirmationToken = _confirmation.Token;
        state.ConfirmationTarget = _confirmation.Target;
        state.ConfirmationFactor = _confirmation.Factor;
        return state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        if (_closed || IsShutDown)
            return;
        if (!system.CanAdminister(Player))
        {
            Close();
            return;
        }

        base.HandleMessage(msg);
        if (_closed || IsShutDown)
            return;

        switch (msg)
        {
            case MarketAdminRefreshMessage:
                settings.RefreshLoad();
                StateDirty();
                break;
            case MarketAdminQueryMessage query:
                if (!system.IsValidQuery(query))
                {
                    Complete(false, "economy-admin-error-query");
                    return;
                }
                _search = query.Search.Trim();
                _group = query.Group;
                _page = query.Page;
                _pageSize = query.PageSize;
                StateDirty();
                break;
            case MarketAdminApplySettingsMessage:
            case MarketAdminSetQuoteMessage:
            case MarketAdminResetQuoteMessage:
            case MarketAdminResetGroupMessage:
            case MarketAdminBeginResetAllMessage:
                BeginMutation(msg);
                break;
            case MarketAdminConfirmMutationMessage confirm:
                if (_confirmation.Kind != MarketAdminMutationKind.None &&
                    !CanMutate(_confirmation.Kind, out var failure))
                {
                    _confirmation.Cancel();
                    Complete(false, failure);
                    return;
                }
                if (!_confirmation.TryConfirm(confirm.Stage, confirm.Token, timing.RealTime, out var mutation))
                {
                    Complete(false, "economy-admin-error-confirmation");
                    return;
                }
                if (mutation != null)
                    CommitMutation(mutation);
                StateDirty();
                break;
            case MarketAdminCancelMutationMessage:
                CancelPendingMutation();
                StateDirty();
                break;
        }
    }

    private void BeginMutation(EuiMessageBase mutation)
    {
        _confirmation.Cancel();
        _status = null;
        _statusSuccess = false;
        var kind = mutation is MarketAdminApplySettingsMessage
            ? MarketAdminMutationKind.ApplySettings
            : MarketAdminMutationKind.SetQuote;
        if (!CanMutate(kind, out var failure))
        {
            Complete(false, failure);
            return;
        }

        if (mutation is MarketAdminApplySettingsMessage apply && apply.Revision != settings.GetSnapshot().Revision)
        {
            Complete(false, "economy-admin-error-conflict");
            return;
        }

        if (!_confirmation.TryBegin(mutation, timing.RealTime))
        {
            Complete(false, "economy-admin-error-invalid");
            return;
        }
        StateDirty();
    }

    private bool CanMutate(MarketAdminMutationKind kind, out string? failure)
    {
        return kind == MarketAdminMutationKind.ApplySettings
            ? system.CanMutateSettings(Player, out failure)
            : system.CanMutateQuotes(Player, out failure);
    }

    private void CommitMutation(EuiMessageBase mutation)
    {
        // The challenge has already consumed its last token and detached its immutable request.
        _status = null;
        _statusSuccess = false;
        switch (mutation)
        {
            case MarketAdminApplySettingsMessage apply:
                system.ApplySettings(Player, apply, Complete);
                break;
            case MarketAdminSetQuoteMessage quote:
                system.SetQuote(Player, quote.MarketKey, quote.Factor, Complete);
                break;
            case MarketAdminResetQuoteMessage quote:
                system.ResetQuote(Player, quote.MarketKey, Complete);
                break;
            case MarketAdminResetGroupMessage group:
                system.ResetGroup(Player, group.Group, Complete);
                break;
            case MarketAdminBeginResetAllMessage:
                system.ResetAllQuotes(Player, Complete);
                break;
        }
    }

    private void CancelPendingMutation()
    {
        if (_confirmation.Kind == MarketAdminMutationKind.None)
            return;

        _confirmation.Cancel();
        _statusSuccess = false;
        _status = "economy-admin-cancelled";
    }

    private void Complete(bool success, string? status)
    {
        // EUI disconnection also calls Closed without setting BaseEui.IsShutDown.
        if (_closed || IsShutDown)
            return;
        if (!system.CanAdminister(Player))
        {
            Close();
            return;
        }

        _statusSuccess = success;
        _status = status ?? (success ? "economy-admin-saved" : "economy-admin-error-save");
        StateDirty();
    }

    private void OnSettingsChanged()
    {
        CancelPendingMutation();
        StateDirty();
    }

    private void OnQuotesChanged()
    {
        CancelPendingMutation();
        StateDirty();
    }

    private void OnPermissionsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !system.CanAdminister(Player))
            Close();
    }
}
