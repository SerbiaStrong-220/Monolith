// SS220 chat bans, adapted to Exodus with awaited operations and server validation.
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Chat;
using Content.Shared.Database._Exodus.Chat;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public partial interface IBanManager
{
    event Action<int>? ChatBanChanged;
    bool CanSendChat(ICommonSession player, ChatChannel channel);
    bool IsChatBanned(ICommonSession player, BannableChats chat);
    Task<BanDef?> TryCreateChatsBanAsync(CreateChatsBanInfo info, ICommonSession? admin);
    Task<bool> TryPardonChatsBanAsync(int banId, ICommonSession? admin);
    Task RefreshChatBanAsync(int banId);
}

[Access(typeof(BanManager), Other = AccessPermissions.Execute)]
public sealed class CreateChatsBanInfo(string reason) : CreateBanInfo(reason)
{
    internal readonly HashSet<BannableChats> Chats = [];

    public CreateChatsBanInfo AddChat(BannableChats chat)
    {
        Chats.Add(chat);
        return this;
    }
}
