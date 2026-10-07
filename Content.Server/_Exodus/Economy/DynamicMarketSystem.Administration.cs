using System.Threading.Tasks;

namespace Content.Server._Exodus.Economy;

public sealed partial class DynamicMarketSystem
{
    private Task _adminQuoteTask = Task.CompletedTask;

    public bool AdminQuotesBusy { get; private set; }
    public bool AdminQuotesReady => _settings.Ready && _loadCompleted;
    public bool QuotesPersistenceEnabled => _persist;
    /// <summary>
    /// Trading waits for saved settings and quotes so an early transaction cannot replace a saved factor.
    /// An explicit reset supplies the authoritative empty state; disabled persistence needs no quote load.
    /// </summary>
    public bool Ready => _settings.Ready && (!_persist || _loadCompleted || _blockLoadApply);

    /// <summary>
    /// Persist an administrative edit before publishing it. Ordinary trading may continue until
    /// publication; periodic writes are serialized behind this operation and keep their dirty keys.
    /// </summary>
    public void ApplyAdminQuotes(
        IReadOnlyDictionary<string, double>? factors,
        IReadOnlyCollection<string>? resetKeys,
        bool resetAll,
        Action<bool, string?> completed)
    {
        if (!_persist || !_settings.Ready || !_loadCompleted || AdminQuotesBusy || _settings.Saving || _shuttingDown)
        {
            completed(false, "economy-admin-error-busy");
            return;
        }

        var upserts = new List<(string MarketKey, double Factor, float Trend)>();
        var deletes = new HashSet<string>(StringComparer.Ordinal);
        if (resetKeys != null)
        {
            foreach (var key in resetKeys)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > 256)
                {
                    completed(false, "economy-admin-error-invalid");
                    return;
                }
                deletes.Add(key);
            }
        }
        if (factors != null)
        {
            foreach (var (key, factor) in factors)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || !double.IsFinite(factor) ||
                    factor < _minFactor || factor > _maxFactor || deletes.Contains(key))
                {
                    completed(false, "economy-admin-error-invalid");
                    return;
                }
                upserts.Add((key, factor, (float) (factor - GetFactor(key))));
            }
        }
        if ((resetAll && (upserts.Count > 0 || deletes.Count > 0)) ||
            (!resetAll && upserts.Count == 0 && deletes.Count == 0))
        {
            completed(false, "economy-admin-error-invalid");
            return;
        }

        AdminQuotesBusy = true;
        _adminQuoteTask = SaveAdminQuotesAsync(_flushTask, upserts, deletes, resetAll, completed);
    }

    private async Task SaveAdminQuotesAsync(
        Task precedingFlush,
        List<(string MarketKey, double Factor, float Trend)> upserts,
        HashSet<string> deletes,
        bool clear,
        Action<bool, string?> completed)
    {
        string? error = null;
        try
        {
            await precedingFlush.ConfigureAwait(false);
            await _db.SaveEconomyMarketQuotes(upserts, deletes, clear, _databaseCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }

        // Completing this task includes publication so shutdown cannot write an old in-memory snapshot.
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                if (_shuttingDown)
                    return;
                AdminQuotesBusy = false;
                if (error != null)
                {
                    Log.Error($"Could not persist administrative market quote changes: {error}");
                    completed(false, "economy-admin-error-save");
                    return;
                }
                if (clear)
                {
                    _quotes.Clear();
                    _dirtyKeys.Clear();
                    _deletedKeys.Clear();
                    _pendingFullClear = false;
                    _blockLoadApply = true;
                }
                foreach (var key in deletes)
                {
                    _quotes.Remove(key);
                    _dirtyKeys.Remove(key);
                    // Keep a tombstone if an earlier failed full replacement still has to be retried.
                    _deletedKeys.Add(key);
                }
                foreach (var (key, factor, _) in upserts)
                    SetFactor(key, factor);
                completed(true, null);
            }
            finally
            {
                published.SetResult();
            }
        });
        await published.Task.ConfigureAwait(false);
    }
}
