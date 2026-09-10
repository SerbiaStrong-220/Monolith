using Content.Shared.Database._Exodus.Chat;
using Robust.Shared.Localization;

namespace Content.Shared._Exodus.Chat;

public static class ChatBanNames
{
    public static string Get(BannableChats chat) => Loc.GetString($"chat-ban-channel-{chat.ToString().ToLowerInvariant()}");
}
