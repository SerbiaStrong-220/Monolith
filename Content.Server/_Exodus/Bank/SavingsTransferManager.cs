using System;
using Content.Server._Mono.MonoCoins;
using Content.Server._NF.Bank;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._Exodus.Bank;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Bank;

/// <summary>
/// Handles lobby-time transfers between a character's main bank account and the account-wide
/// savings (MonoCoins). The server is authoritative: it both moves the bank balance (via the
/// character's preferences) and the savings, then echoes the new bank balance back to the client
/// so the local profile stays in sync.
/// </summary>
public sealed class SavingsTransferManager
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private MonoCoinsManager _coins = default!;
    [Dependency] private IEntityManager _entMan = default!;

    private GameTicker _ticker = default!;
    private BankSystem _bank = default!;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgSavingsTransferRequest>(OnTransferRequest);
        _net.RegisterNetMessage<MsgSavingsTransferState>();

        // Cache the systems once; the entity systems are already up at this point.
        _ticker = _entMan.System<GameTicker>();
        _bank = _entMan.System<BankSystem>();
    }

    private void OnTransferRequest(MsgSavingsTransferRequest msg)
    {
        // Reject the no-op and int.MinValue (whose negation overflows back to a negative number).
        if (msg.Amount == 0 || msg.Amount == int.MinValue)
            return;

        if (!_player.TryGetSessionByChannel(msg.MsgChannel, out var session))
            return;

        // Only allow transfers from the lobby. In-round the live BankAccountComponent is not updated
        // by this path, so moving profile money to savings mid-round would duplicate it.
        if (_ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) &&
            status == PlayerGameStatus.JoinedGame)
            return;

        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
            return;

        if (msg.Amount > 0)
        {
            // Main account -> savings. Clamp to what's on the main account: asking for more
            // than you have just transfers everything available.
            var moveAmount = Math.Min(msg.Amount, profile.BankBalance);
            if (moveAmount > 0 &&
                _bank.TryBankWithdraw(session, prefs, profile, moveAmount, out _, spendLongTerm: false))
                _ = _coins.AddMonoCoinsAsync(session.UserId, moveAmount);
        }
        else
        {
            // Savings -> main account. Clamp to available savings. (int.MinValue rejected at the top.)
            var savings = _coins.GetMonoCoinsBalance(session.UserId) ?? 0;
            var moveAmount = (int)Math.Min(-msg.Amount, savings);
            if (moveAmount > 0 &&
                _bank.TryBankDeposit(session, prefs, profile, moveAmount, out _))
                _ = _coins.AddMonoCoinsAsync(session.UserId, -moveAmount);
        }

        // Echo back the authoritative bank balance so the client updates its local profile copy.
        if (!_prefs.TryGetCachedPreferences(session.UserId, out var updatedPrefs) ||
            updatedPrefs.SelectedCharacter is not HumanoidCharacterProfile updatedProfile)
            return;

        _net.ServerSendMessage(new MsgSavingsTransferState { BankBalance = updatedProfile.BankBalance }, msg.MsgChannel);
    }
}
