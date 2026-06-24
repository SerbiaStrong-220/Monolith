using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Bank;

/// <summary>
/// Client -> server request to move money between the currently selected character's main bank
/// account and the account-wide savings (MonoCoins).
/// A positive <see cref="Amount"/> moves from the main account into savings;
/// a negative <see cref="Amount"/> moves the other way.
/// </summary>
public sealed class MsgSavingsTransferRequest : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public int Amount;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Amount = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Amount);
    }
}

/// <summary>
/// Server -> client update with the selected character's main bank balance after a transfer.
/// Savings (MonoCoins) are synced separately through MsgMonoCoins.
/// </summary>
public sealed class MsgSavingsTransferState : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public int BankBalance;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        BankBalance = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(BankBalance);
    }
}
