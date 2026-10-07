using System.Text.Json;
using System.Threading.Tasks;
using System.Threading; // Exodus log export
using Content.Server._Exodus.Administration.LogExport; // Exodus log export
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.Administration.Logs;

namespace Content.Server.Administration.Logs;

public interface IAdminLogManager : ISharedAdminLogManager
{
    void Initialize();
    Task Shutdown();
    void Update();

    void RoundStarting(int id);
    void RunLevelChanged(GameRunLevel level);

    Task<List<SharedAdminLog>> All(LogFilter? filter = null, Func<List<SharedAdminLog>>? listProvider = null);
    IAsyncEnumerable<string> AllMessages(LogFilter? filter = null);
    IAsyncEnumerable<JsonDocument> AllJson(LogFilter? filter = null);
    Task<Round> Round(int roundId);
    Task<List<SharedAdminLog>> CurrentRoundLogs(LogFilter? filter = null);
    IAsyncEnumerable<string> CurrentRoundMessages(LogFilter? filter = null);
    IAsyncEnumerable<JsonDocument> CurrentRoundJson(LogFilter? filter = null);
    Task<Round> CurrentRound();
    Task<int> CountLogs(int round);

    // Exodus-begin: bounded round snapshots for full log exports.
    Task<AdminLogExportSnapshot?> CreateExportSnapshotAsync(int roundId, CancellationToken cancel = default);
    Task<IReadOnlyList<SharedAdminLog>> ReadExportPageAsync(AdminLogExportSnapshot snapshot, int offset, int afterId, int limit, CancellationToken cancel = default);
    // Exodus-end
}
