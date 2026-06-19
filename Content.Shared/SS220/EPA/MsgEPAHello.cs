// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

// Sent by server during handshake when connection is estabilished and engine authorization is handled
public sealed class MsgEPAHello : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public bool ShouldAuth;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ShouldAuth = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(ShouldAuth);
    }
}
