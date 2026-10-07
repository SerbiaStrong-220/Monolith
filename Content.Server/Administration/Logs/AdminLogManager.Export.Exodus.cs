// Exodus: stable, bounded export reads without forcing or changing the normal log writer.
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;
using Robust.Shared.Asynchronous;

namespace Content.Server.Administration.Logs;

public sealed partial class AdminLogManager
{
    [Dependency] private ITaskManager _exportTasks = default!;

    public async Task<AdminLogExportSnapshot?> CreateExportSnapshotAsync(int roundId, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        if (roundId <= 0)
            return null;

        var cached = await OnExportMainThread<AdminLogExportSnapshot?>(() =>
        {
            if (!TryGetCache(roundId, out var cache))
                return null;

            return new AdminLogExportSnapshot(roundId, cache.Count, cache.Count == 0 ? 0 : cache[^1].Id, DateTime.UtcNow)
            {
                CacheIdentity = cache,
            };
        }, cancel).ConfigureAwait(false);

        return cached ?? await _db.GetAdminLogExportSnapshot(roundId, cancel).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SharedAdminLog>> ReadExportPageAsync(
        AdminLogExportSnapshot snapshot,
        int offset,
        int afterId,
        int limit,
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, snapshot.Count);
        AdminLogExportDataBounds.ValidatePage(snapshot.RoundId, afterId, snapshot.LastLogId, limit);

        if (snapshot.CacheIdentity == null)
            return _db.GetAdminLogExportPage(snapshot.RoundId, afterId, snapshot.LastLogId, limit, cancel);

        return OnExportMainThread<IReadOnlyList<SharedAdminLog>>(() =>
        {
            if (!TryGetCache(snapshot.RoundId, out var cache) ||
                !ReferenceEquals(cache, snapshot.CacheIdentity) || cache.Count < snapshot.Count)
                throw new AdminLogExportDataException("admin-logs-export-error-cache-expired");

            var count = Math.Min(limit, snapshot.Count - offset);
            var page = new List<SharedAdminLog>(count);
            var messageBytes = 0;
            var players = 0;
            for (var i = 0; i < count; i++)
            {
                var log = cache[offset + i];
                var bytes = AdminLogExportDataBounds.GetMessageBytes(log);
                if (page.Count > 0 &&
                    (messageBytes + bytes > AdminLogExportLimits.RecordBytes ||
                     players + log.Players.Length > AdminLogExportDataBounds.MaxPagePlayers))
                    break;

                page.Add(log);
                messageBytes += bytes;
                players += log.Players.Length;
            }

            return page;
        }, cancel);
    }

    private Task<T> OnExportMainThread<T>(Func<T> action, CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _exportTasks.RunOnMainThread(() =>
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancel);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task.WaitAsync(cancel);
    }
}
