using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database._Exodus.SafetyDepositBox;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<List<SafetyDepositAdminSummary>> GetAdminSafetyDepositBoxes(List<Guid> ownerIds, CancellationToken cancel = default);
    Task AddSafetyDepositAdminAudit(SafetyDepositAdminAudit audit, CancellationToken cancel = default);
    Task CompleteSafetyDepositAdminAudit(Guid operationId, string result, string details, CancellationToken cancel = default);
    Task<SafetyDepositAdminAudit?> GetSafetyDepositAdminAudit(Guid operationId, CancellationToken cancel = default);
    Task<List<SafetyDepositAdminAudit>> GetSafetyDepositAdminAudits(Guid boxId, int limit = 50, CancellationToken cancel = default);
    Task<bool> TryAdminReplaceSafetyDepositBoxItems(WayfarerSafetyDepositBox expected, List<string> replacementData, SafetyDepositAdminAudit audit, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<SafetyDepositAdminSummary>> GetAdminSafetyDepositBoxes(List<Guid> ownerIds, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAdminSafetyDepositBoxes(ownerIds, cancel));
    }

    public Task AddSafetyDepositAdminAudit(SafetyDepositAdminAudit audit, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddSafetyDepositAdminAudit(audit, cancel));
    }

    public Task CompleteSafetyDepositAdminAudit(Guid operationId, string result, string details, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.CompleteSafetyDepositAdminAudit(operationId, result, details, cancel));
    }

    public Task<SafetyDepositAdminAudit?> GetSafetyDepositAdminAudit(Guid operationId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetSafetyDepositAdminAudit(operationId, cancel));
    }

    public Task<List<SafetyDepositAdminAudit>> GetSafetyDepositAdminAudits(Guid boxId, int limit = 50, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetSafetyDepositAdminAudits(boxId, limit, cancel));
    }

    public Task<bool> TryAdminReplaceSafetyDepositBoxItems(WayfarerSafetyDepositBox expected, List<string> replacementData, SafetyDepositAdminAudit audit, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.TryAdminReplaceSafetyDepositBoxItems(expected, replacementData, audit, cancel));
    }
}
