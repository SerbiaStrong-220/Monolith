// (c) Space Exodus Team - EXDS-RL with CLA

using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.SS220.EPA;

public sealed partial class MsgEPACheckSession : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public bool Check;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Check = buffer.ReadBoolean();
        buffer.ReadPadBits();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Check);
    }
}
