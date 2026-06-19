using Robust.Shared.Network;

namespace Content.Server.SS220.Extensions;

public static class INetChannelExt
{
    public static string ToPrettyString(this INetChannel channel)
    {
        return $"{channel.UserName} ({channel.RemoteEndPoint}, {channel.UserId})";
    }
}
