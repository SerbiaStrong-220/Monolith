// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

// Sent by server if client passed validation
public sealed class MsgEPAAccept : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Guid UserId;
    public string Username = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        UserId = buffer.ReadGuid();
        Username = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(UserId);
        buffer.Write(Username);
    }
}
