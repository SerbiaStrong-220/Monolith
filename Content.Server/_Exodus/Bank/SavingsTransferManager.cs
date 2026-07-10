using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Mono.MonoCoins;
using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Preferences.Managers;
using Content.Shared._Exodus.Bank;
using Content.Shared.Database;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Bank;

/// <summary>
/// Handles lobby-time transfers between a character's main bank account and the account-wide
/// savings (MonoCoins). The server is authoritative: it moves the bank balance (via the character's
/// preferences) and the savings, then echoes the new bank balance back to the client.
/// </summary>
public sealed class SavingsTransferManager
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MonoCoinsManager _coins = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private BankSystem _bank = default!;

    private readonly HashSet<NetUserId> _inFlight = [];

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgSavingsTransferRequest>(OnTransferRequest);
        _net.RegisterNetMessage<MsgSavingsTransferState>();
        _bank = _entMan.System<BankSystem>();
    }

    private async void OnTransferRequest(MsgSavingsTransferRequest msg)
    {
        if (!_player.TryGetSessionByChannel(msg.MsgChannel, out var session))
            return;

        if (!_inFlight.Add(session.UserId))
            return;
        try
        {
            await ProcessTransfer(session, msg);
        }
        catch (Exception e)
        {
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.High,
                $"{session:player} savings transfer threw and was aborted: {e}");
        }
        finally
        {
            _inFlight.Remove(session.UserId);
        }
    }

    private async Task ProcessTransfer(ICommonSession session, MsgSavingsTransferRequest msg)
    {
        // Reject the no-op and int.MinValue (whose negation overflows back to a negative number).
        if (msg.Amount == 0 || msg.Amount == int.MinValue)
            return;

        // Only allow transfers In lobby.
        if (session.AttachedEntity != null)
            return;

        if (!TryGetProfile(session, out var profile))
            return;

        if (msg.Amount > 0)
            await AccountToSavings(session, profile, msg.Amount);
        else
            await SavingsToAccount(session, -(long)msg.Amount);

        EchoBankBalance(session);
    }

    private async Task AccountToSavings(ICommonSession session, HumanoidCharacterProfile profile, int requested)
    {
        var transferable = Math.Max(0, profile.BankBalance - HumanoidCharacterProfile.DefaultBalance);
        var moveAmount = Math.Min(requested, transferable);
        if (moveAmount <= 0)
            return;

        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs) ||
            !_bank.TryBankWithdraw(session, prefs, profile, moveAmount, out _, spendLongTerm: false))
        {
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.Medium,
                $"{session:player} failed to move {moveAmount} from account to savings (withdraw rejected).");
            return;
        }

        long credited;
        try
        {
            var before = await _coins.GetMonoCoinsBalanceAsync(session.UserId);
            var after = await _coins.AddMonoCoinsAsync(session.UserId, moveAmount);
            credited = after - before;
        }
        catch (Exception e)
        {
            var refunded = TryRefundToAccount(session, moveAmount);
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.High,
                $"{session:player} account->savings of {moveAmount} FAILED on savings credit ({e.Message}); account refund {(refunded ? "ok" : "ALSO FAILED")}.");
            return;
        }

        // Refund bank anything that did not actually reach savings.
        var shortfall = moveAmount - (int)Math.Clamp(credited, 0, moveAmount);
        if (shortfall > 0)
        {
            var refunded = TryRefundToAccount(session, shortfall);
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.High,
                $"{session:player} account->savings credited only {credited}/{moveAmount}; refunded {shortfall} to account ({(refunded ? "ok" : "FAILED")}).");
            return;
        }

        _adminLogger.Add(LogType.SavingsTransfer, LogImpact.Medium,
            $"{session:player} moved {moveAmount} from account to savings.");
    }

    private async Task SavingsToAccount(ICommonSession session, long requested)
    {
        int move;
        try
        {
            var before = await _coins.GetMonoCoinsBalanceAsync(session.UserId);
            move = (int)Math.Min(requested, before);
            if (move <= 0)
                return;

            await _coins.AddMonoCoinsAsync(session.UserId, -move);
        }
        catch (Exception e)
        {
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.High,
                $"{session:player} savings->account FAILED on savings debit ({e.Message}); nothing moved.");
            return;
        }

        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile profile ||
            !_bank.TryBankDeposit(session, prefs, profile, move, out _))
        {
            var refunded = await TryRefundToSavings(session, move);
            _adminLogger.Add(LogType.SavingsTransfer, LogImpact.High,
                $"{session:player} savings->account of {move} FAILED on account deposit; savings refund {(refunded ? "ok" : "ALSO FAILED")}.");
            return;
        }

        _adminLogger.Add(LogType.SavingsTransfer, LogImpact.Medium,
            $"{session:player} moved {move} from savings to account.");
    }

    private bool TryRefundToAccount(ICommonSession session, int amount)
    {
        return _prefs.TryGetCachedPreferences(session.UserId, out var prefs) &&
               prefs.SelectedCharacter is HumanoidCharacterProfile profile &&
               _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }

    private async Task<bool> TryRefundToSavings(ICommonSession session, int amount)
    {
        try
        {
            await _coins.AddMonoCoinsAsync(session.UserId, amount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetProfile(ICommonSession session, out HumanoidCharacterProfile profile)
    {
        profile = default!;
        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile selected)
            return false;

        profile = selected;
        return true;
    }

    private void EchoBankBalance(ICommonSession session)
    {
        if (!TryGetProfile(session, out var profile))
            return;

        _net.ServerSendMessage(new MsgSavingsTransferState { BankBalance = profile.BankBalance }, session.Channel);
    }
}
