// Exodus: idempotent revocation for the SS220 chat ban port.
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<bool> TryPardonChatBanAsync(int banId, NetUserId? admin, DateTimeOffset time);
}

public sealed partial class ServerDbManager
{
    public Task<bool> TryPardonChatBanAsync(int banId, NetUserId? admin, DateTimeOffset time)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.TryPardonChatBanAsync(banId, admin, time));
    }
}
