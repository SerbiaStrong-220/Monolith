// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

// Sent by server in response to MsgEPACreateSession
public sealed class MsgEPACreateSessionRes : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public string AuthUrl = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        AuthUrl = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(AuthUrl);
    }
}
