using System;
using Content.Shared._Exodus.Bank;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client._Exodus.Bank;

/// <summary>
/// Client-side counterpart of the savings transfer system. Sends transfer requests to the server
/// and raises <see cref="BankBalanceUpdated"/> when the server reports the new main bank balance.
/// </summary>
public sealed class SavingsTransferManager
{
    [Dependency] private INetManager _net = default!;

    /// <summary>
    /// Raised with the new main bank balance after the server processes a transfer.
    /// </summary>
    public event Action<int>? BankBalanceUpdated;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgSavingsTransferState>(OnState);
        _net.RegisterNetMessage<MsgSavingsTransferRequest>();
    }

    private void OnState(MsgSavingsTransferState msg)
    {
        BankBalanceUpdated?.Invoke(msg.BankBalance);
    }

    /// <summary>
    /// Requests a transfer. Positive moves main account -> savings, negative moves savings -> main account.
    /// </summary>
    public void RequestTransfer(int amount)
    {
        _net.ClientSendMessage(new MsgSavingsTransferRequest { Amount = amount });
    }
}
