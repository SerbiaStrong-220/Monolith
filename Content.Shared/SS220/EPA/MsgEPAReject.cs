// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

public sealed partial class MsgEPAReject : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public string Reason = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Reason = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Reason);
    }
}
