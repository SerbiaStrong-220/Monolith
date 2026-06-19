// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

// Sent by client during handshake after MsgEPAHello is received if user isn't authorized
public sealed class MsgEPACreateSession : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
    }
}
