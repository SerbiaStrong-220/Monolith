// SS220 chat ban synchronization; Exodus sends deadlines on the synchronized server clock.
using Content.Shared.Database._Exodus.Chat;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Chat;

public readonly record struct ChatBanStatus(BannableChats Chat, TimeSpan? ExpiresAt);

public sealed class MsgChatsBans : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;
    public readonly List<ChatBanStatus> Bans = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var count = buffer.ReadByte();
        for (var i = 0; i < count; i++)
        {
            var chat = (BannableChats)buffer.ReadByte();
            var ticks = buffer.ReadInt64();
            Bans.Add(new ChatBanStatus(chat, ticks < 0 ? null : TimeSpan.FromTicks(ticks)));
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte)Bans.Count);
        foreach (var ban in Bans)
        {
            buffer.Write((byte)ban.Chat);
            buffer.Write(ban.ExpiresAt?.Ticks ?? -1);
        }
    }
}
