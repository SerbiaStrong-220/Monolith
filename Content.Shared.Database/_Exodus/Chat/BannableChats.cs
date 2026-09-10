// SS220 chat bans, adapted from SerbiaStrong-220/space-station-14 at ddb370a431c2694cc90a71fbd29fa2b4fedf22ec.
using System;
using System.Collections.Generic;

namespace Content.Shared.Database._Exodus.Chat;

public enum BannableChats : byte
{
    Invalid,
    LOOC,
    OOC,
    Dead,
}

public static class ChatBanLimits
{
    public const int MaxReasonLength = 1024;
    public const uint MaxDurationMinutes = 5256000;

    public static bool ValidChat(BannableChats chat) => chat != BannableChats.Invalid && Enum.IsDefined(chat);

    public static bool ValidChats(IReadOnlyCollection<BannableChats> chats)
    {
        if (chats.Count == 0 || chats.Count >= Enum.GetValues<BannableChats>().Length)
            return false;

        var unique = new HashSet<BannableChats>();
        foreach (var chat in chats)
        {
            if (!ValidChat(chat) || !unique.Add(chat))
                return false;
        }

        return true;
    }
}
