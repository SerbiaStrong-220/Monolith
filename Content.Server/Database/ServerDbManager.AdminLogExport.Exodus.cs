// Exodus: bounded administrative log export database entry points.
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<AdminLogExportSnapshot?> GetAdminLogExportSnapshot(int roundId, CancellationToken cancel = default);
    Task<IReadOnlyList<SharedAdminLog>> GetAdminLogExportPage(int roundId, int afterId, int lastLogId, int limit, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<AdminLogExportSnapshot?> GetAdminLogExportSnapshot(int roundId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAdminLogExportSnapshot(roundId, cancel));
    }

    public Task<IReadOnlyList<SharedAdminLog>> GetAdminLogExportPage(int roundId, int afterId, int lastLogId, int limit, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAdminLogExportPage(roundId, afterId, lastLogId, limit, cancel));
    }
}
