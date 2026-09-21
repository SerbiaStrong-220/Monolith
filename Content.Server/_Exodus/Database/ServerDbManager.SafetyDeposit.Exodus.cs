using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task SetSafetyDepositBoxWithdrawnItems(Guid boxId, int roundId, List<string> retainedData, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task SetSafetyDepositBoxWithdrawnItems(Guid boxId, int roundId, List<string> retainedData, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetSafetyDepositBoxWithdrawnItems(boxId, roundId, retainedData, cancel));
    }
}
