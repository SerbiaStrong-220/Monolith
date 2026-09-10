// SS220 chat requirements, adapted to use the synchronized server clock, including packet transit time.
using Content.Shared._Exodus.Chat;
using Content.Shared.Chat;
using Content.Shared.Database._Exodus.Chat;
using Robust.Client;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Chat;

public sealed class ChatRequirementsManager
{
    [Dependency] private IBaseClient _client = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<BannableChats, TimeSpan?> _chatBans = new();
    private bool _loaded;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgChatsBans>(ReceiveChatBans);
        _client.RunLevelChanged += (_, args) =>
        {
            if (args.NewLevel != ClientRunLevel.Initialize)
                return;
            _chatBans.Clear();
            _loaded = false;
        };
    }

    private void ReceiveChatBans(MsgChatsBans message)
    {
        _chatBans.Clear();
        foreach (var ban in message.Bans)
        {
            if (ChatBanLimits.ValidChat(ban.Chat))
                _chatBans[ban.Chat] = ban.ExpiresAt;
        }

        _loaded = true;
    }

    public bool IsBanned(ChatSelectChannel channel)
    {
        var chat = channel switch
        {
            ChatSelectChannel.OOC => BannableChats.OOC,
            ChatSelectChannel.LOOC => BannableChats.LOOC,
            ChatSelectChannel.Dead => BannableChats.Dead,
            _ => BannableChats.Invalid,
        };

        if (chat == BannableChats.Invalid)
            return false;

        return !_loaded || _chatBans.TryGetValue(chat, out var expiry) &&
            (expiry == null || expiry > _timing.ServerTime);
    }
}
