// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

// Sent by server when new session for user was created with token

public sealed class MsgEPANewSession : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public string Token = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Token = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Token);
    }
}
