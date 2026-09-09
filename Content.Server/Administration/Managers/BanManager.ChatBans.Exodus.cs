// SS220 chat bans, ported from SerbiaStrong-220/space-station-14 at ddb370a431c2694cc90a71fbd29fa2b4fedf22ec.
// Exodus: enforce expiry per message, protect asynchronous cache loads, and await moderation operations.
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Exodus.Chat;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Database._Exodus.Chat;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public sealed partial class BanManager
{
    [Dependency] private IAdminManager _chatBanAdmins = default!;
    public event Action<int>? ChatBanChanged;

    private readonly Dictionary<ICommonSession, ChatBanCache> _cachedChatsBans = new();
    private readonly HashSet<int> _chatUnbansInProgress = new();
    private int _chatBanRevision;

    private void InitializeChatBans()
    {
        _netManager.RegisterNetMessage<MsgChatsBans>();
        _userDbData.AddOnLoadPlayer(CacheChatBans);
        _userDbData.AddOnPlayerDisconnect(player => _cachedChatsBans.Remove(player));
    }

    private async Task CacheChatBans(ICommonSession player, CancellationToken cancel)
    {
        var cache = new ChatBanCache();
        _cachedChatsBans[player] = cache;
        int revision;
        do
        {
            revision = _chatBanRevision;
            var channel = player.Channel;
            cache.Bans = await _db.GetBansAsync(channel.RemoteEndPoint.Address, player.UserId,
                channel.UserData.HWId, channel.UserData.ModernHWIds, false, BanType.Chat);
            cancel.ThrowIfCancellationRequested();
        } while (revision != _chatBanRevision);

        cache.Loaded = true;
        SendChatsBans(player, cache);
    }

    public bool IsChatBanned(ICommonSession player, BannableChats chat)
    {
        if (!ChatBanLimits.ValidChat(chat))
            return false;

        return !_cachedChatsBans.TryGetValue(player, out var cache) || !cache.Loaded ||
               FindChatBan(cache, chat, DateTimeOffset.UtcNow) != null;
    }

    public bool CanSendChat(ICommonSession player, ChatChannel channel)
    {
        var chat = channel switch
        {
            ChatChannel.OOC => BannableChats.OOC,
            ChatChannel.LOOC => BannableChats.LOOC,
            ChatChannel.Dead => BannableChats.Dead,
            _ => BannableChats.Invalid,
        };
        if (chat == BannableChats.Invalid)
            return true;

        if (!_cachedChatsBans.TryGetValue(player, out var cache) || !cache.Loaded)
        {
            _chat.DispatchServerMessage(player, Loc.GetString("chat-ban-loading"));
            return false;
        }

        var ban = FindChatBan(cache, chat, DateTimeOffset.UtcNow);
        if (ban == null)
            return true;

        _chat.DispatchServerMessage(player, Loc.GetString("chat-ban-blocked",
            ("chat", ChatBanNames.Get(chat)), ("expires", FormatChatBanExpiry(ban)), ("reason", ban.Reason)));
        return false;
    }

    private static BanDef? FindChatBan(ChatBanCache cache, BannableChats chat, DateTimeOffset now)
    {
        BanDef? active = null;
        for (var i = cache.Bans.Count - 1; i >= 0; i--)
        {
            var ban = cache.Bans[i];
            if (ban.Unban != null || ban.ExpirationTime <= now)
            {
                cache.Bans.RemoveAt(i);
                continue;
            }

            if (ban.Chats is not { } chats || !chats.Contains(chat))
                continue;

            if (active == null || ban.ExpirationTime == null || ban.ExpirationTime > active.ExpirationTime)
                active = ban;
        }

        return active;
    }

    public async Task<BanDef?> TryCreateChatsBanAsync(CreateChatsBanInfo info, ICommonSession? admin)
    {
        if (!MayManageChatBans(admin) || !ChatBanLimits.ValidChats(info.Chats) ||
            string.IsNullOrWhiteSpace(info.Reason) || info.Reason.Length > ChatBanLimits.MaxReasonLength ||
            info.Duration <= TimeSpan.Zero || info.Duration > TimeSpan.FromMinutes(ChatBanLimits.MaxDurationMinutes) ||
            info.Severity is { } severity && !Enum.IsDefined(severity))
            return null;

        if (info.Users.Count == 0 && info.AddressRanges.Count == 0 && info.HWIds.Count == 0)
            return null;

        info.WithSeverity(info.Severity ?? NoteSeverity.Medium);
        info.WithBanningAdmin(admin?.UserId);
        info.WithReason(info.Reason.Trim());
        // Use the general ban records and matching selectors, as in SS220.
        var (ban, _) = await CreateBanDef(info, BanType.Chat, null, [.. info.Chats]);
        if (!MayManageChatBans(admin))
            return null;

        ban = await _db.AddBanAsync(ban);
        UpdateChatBanCache(ban);
        ChatBanChanged?.Invoke(ban.Id!.Value);

        var channels = string.Join(", ", info.Chats.Select(ChatBanNames.Get));
        var target = string.Join(", ", info.Users.Select(u => u.UserName));
        var adminName = admin?.Name ?? Loc.GetString("system-user");
        var message = Loc.GetString("chat-ban-success", ("admin", adminName), ("target", target),
            ("chats", channels), ("expires", FormatChatBanExpiry(ban)), ("reason", ban.Reason), ("id", ban.Id!.Value));
        _sawmill.Info(message);
        _chat.SendAdminAlert(message);
        foreach (var session in _playerManager.Sessions)
        {
            if (BanMatchesPlayer(session, ban))
                _chat.DispatchServerMessage(session, Loc.GetString("chat-ban-applied", ("chats", channels),
                    ("expires", FormatChatBanExpiry(ban)), ("reason", ban.Reason)));
        }

        return ban;
    }

    public async Task<bool> TryPardonChatsBanAsync(int banId, ICommonSession? admin)
    {
        if (banId <= 0 || !MayManageChatBans(admin) || !_chatUnbansInProgress.Add(banId))
            return false;

        try
        {
            var ban = await _db.GetBanAsync(banId);
            if (ban is not { Type: BanType.Chat, Unban: null } || !MayManageChatBans(admin))
                return false;

            if (!await _db.TryPardonChatBanAsync(banId, admin?.UserId, DateTimeOffset.UtcNow))
                return false;

            await RefreshChatBanAsync(banId);
            ChatBanChanged?.Invoke(banId);
            var message = Loc.GetString("chat-unban-success", ("id", banId),
                ("admin", admin?.Name ?? Loc.GetString("system-user")));
            _sawmill.Info(message);
            _chat.SendAdminAlert(message);
            foreach (var session in _playerManager.Sessions)
            {
                if (BanMatchesPlayer(session, ban))
                    _chat.DispatchServerMessage(session, Loc.GetString("chat-ban-revoked", ("id", banId)));
            }

            return true;
        }
        finally
        {
            _chatUnbansInProgress.Remove(banId);
        }
    }

    private bool MayManageChatBans(ICommonSession? admin)
    {
        return admin == null || admin.Status != SessionStatus.Disconnected &&
            _chatBanAdmins.HasAdminFlag(admin, AdminFlags.Ban);
    }

    public async Task RefreshChatBanAsync(int banId)
    {
        int revision;
        BanDef? ban;
        do
        {
            revision = _chatBanRevision;
            ban = await _db.GetBanAsync(banId);
        } while (revision != _chatBanRevision);

        if (ban is { Type: BanType.Chat })
            UpdateChatBanCache(ban);
    }

    private void UpdateChatBanCache(BanDef ban)
    {
        _chatBanRevision++;
        foreach (var (session, cache) in _cachedChatsBans)
        {
            var removed = cache.Bans.RemoveAll(existing => existing.Id == ban.Id);
            var matches = BanMatchesPlayer(session, ban);
            if (removed == 0 && !matches)
                continue;

            if (ban.Unban == null && (ban.ExpirationTime == null || ban.ExpirationTime > DateTimeOffset.UtcNow) &&
                matches)
                cache.Bans.Add(ban);

            if (cache.Loaded && session.Status != SessionStatus.Disconnected)
                SendChatsBans(session, cache);
        }
    }

    private void SendChatsBans(ICommonSession session, ChatBanCache cache)
    {
        var now = DateTimeOffset.UtcNow;
        var message = new MsgChatsBans();
        foreach (var chat in Enum.GetValues<BannableChats>())
        {
            if (chat == BannableChats.Invalid || FindChatBan(cache, chat, now) is not { } ban)
                continue;

            message.Bans.Add(new ChatBanStatus(chat, _gameTiming.RealTime + (ban.ExpirationTime - now)));
        }

        _netManager.ServerSendMessage(message, session.Channel);
    }

    private static string FormatChatBanExpiry(BanDef ban)
    {
        return ban.ExpirationTime?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? Loc.GetString("chat-ban-permanent");
    }

    private sealed class ChatBanCache
    {
        public List<BanDef> Bans = new();
        public bool Loaded;
    }
}
