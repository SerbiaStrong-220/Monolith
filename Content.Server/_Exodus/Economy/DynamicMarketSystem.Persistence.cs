// (c) Space Exodus Team - EXDS-RL with CLA
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Asynchronous;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Cross-round DB persistence for the global dynamic market quote store.
/// </summary>
public sealed partial class DynamicMarketSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ITaskManager _taskManager = default!;

    private bool _persist = true;
    private TimeSpan _persistInterval = TimeSpan.FromSeconds(60);
    private TimeSpan _nextPersist;
    private readonly HashSet<string> _dirtyKeys = new();
    private readonly HashSet<string> _deletedKeys = new();
    private bool _loadStarted;
    private bool _loadCompleted;

    /// <summary>
    /// Set by <see cref="ClearAllPersisted"/> so an in-flight DB load cannot repopulate
    /// after an admin/market reset.
    /// </summary>
    private bool _blockLoadApply;

    /// <summary>
    /// Next flush should wipe the entire quotes table (then re-upsert current memory).
    /// Required so ResetAll clears keys that never finished loading into memory.
    /// </summary>
    private bool _pendingFullClear;

    private bool _flushInProgress;
    private bool _forceFlushQueued;
    private TimeSpan _retryAfter;
    private Task _flushTask = Task.CompletedTask;
    private IReadOnlyCollection<string> _flushingDeletes = Array.Empty<string>();
    private bool _flushingFullClear;
    private bool _shuttingDown;
    private readonly CancellationTokenSource _databaseCancellation = new();

    private void InitializePersistence()
    {
        Subs.CVar(_cfg, EXCVars.DynamicMarketPersist, OnPersistCVar, true);
        Subs.CVar(_cfg, EXCVars.DynamicMarketPersistIntervalSeconds, v =>
        {
            var seconds = Math.Max(5.0, SanitizeIntervalSeconds(v, 60.0));
            _persistInterval = TimeSpan.FromSeconds(seconds);
            _nextPersist = _timing.CurTime + _persistInterval;
        }, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartFlush);
        _nextPersist = _timing.CurTime + _persistInterval;

        if (_persist && _settings.Ready)
            _ = LoadFromDatabaseAsync();
    }

    private void OnPersistCVar(bool value)
    {
        var wasPersisting = _persist;
        _persist = value;

        if (!_persist || wasPersisting)
            return;

        _nextPersist = _timing.CurTime + _persistInterval;

        if (_pendingFullClear)
        {
            QueueForceFlush();
        }
        else if (_loadCompleted)
        {
            // Memory may have diverged while persistence was disabled.
            // Replace the stored snapshot with the current in-memory state on re-enable.
            _pendingFullClear = true;
            QueueForceFlush();
        }
        else if (!_loadStarted)
        {
            _ = LoadFromDatabaseAsync();
        }
    }

    private void UpdatePersistence()
    {
        if (!_persist || !_settings.Ready || _shuttingDown || _timing.CurTime < _retryAfter || AdminQuotesBusy)
            return;

        if (!_loadCompleted && !_blockLoadApply)
        {
            if (!_loadStarted)
                _ = LoadFromDatabaseAsync();
            return;
        }

        if ((_forceFlushQueued || _pendingFullClear) && !_flushInProgress)
        {
            _forceFlushQueued = false;
            StartFlush(forceAll: true);
            return;
        }

        if (_timing.CurTime < _nextPersist)
            return;

        _nextPersist += _persistInterval;
        StartFlush();
    }

    private void OnRoundRestartFlush(RoundRestartCleanupEvent ev)
    {
        QueueForceFlush();
    }

    /// <summary>
    /// Called from <see cref="DynamicMarketSystem.Shutdown"/>.
    /// </summary>
    private void ShutdownPersistence()
    {
        FlushForShutdown();
        _databaseCancellation.Dispose();
    }

    /// <summary>
    /// Flush before the database is disposed. Entity system shutdown runs after database shutdown,
    /// so the server entry point calls this first. Only this final shutdown path waits synchronously.
    /// </summary>
    public void FlushForShutdown()
    {
        if (_shuttingDown)
            return;

        if (AdminQuotesBusy)
        {
            _taskManager.BlockWaitOnTask(Task.WhenAny(_adminQuoteTask, Task.Delay(TimeSpan.FromSeconds(10))));
            if (AdminQuotesBusy)
            {
                // A late administrative write must not be overwritten by a stale shutdown snapshot.
                _shuttingDown = true;
                _databaseCancellation.Cancel();
                Log.Error("Timed out waiting for administrative market persistence during shutdown.");
                return;
            }
        }

        _shuttingDown = true;
        _databaseCancellation.Cancel();

        var task = _flushTask;
        var shutdownTimeout = TimeSpan.FromSeconds(10);
        using var timeout = new CancellationTokenSource(shutdownTimeout);
        if (_persist)
        {
            var upserts = new List<(string MarketKey, double Factor, float Trend)>(_quotes.Count);
            foreach (var (key, quote) in _quotes)
            {
                upserts.Add((key, quote.Factor, quote.Trend));
            }

            var deletes = new HashSet<string>(_deletedKeys);
            foreach (var key in _flushingDeletes)
            {
                if (!_quotes.ContainsKey(key))
                    deletes.Add(key);
            }

            task = FlushShutdownSnapshotAsync(task, upserts, deletes,
                _pendingFullClear || _flushingFullClear, timeout.Token);
        }

        // Pump pending continuations while waiting; an ordinary Wait/Result can deadlock the DB wrapper.
        _taskManager.BlockWaitOnTask(Task.WhenAny(task, Task.Delay(shutdownTimeout)));
        if (!task.IsCompleted)
        {
            timeout.Cancel();
            Log.Error("Timed out flushing economy market quotes during shutdown.");
        }
        else if (task.IsFaulted || task.IsCanceled)
        {
            Log.Error($"Failed to flush economy market quotes during shutdown: {task.Exception}");
        }
    }

    private async Task FlushShutdownSnapshotAsync(
        Task previousFlush,
        List<(string MarketKey, double Factor, float Trend)> upserts,
        HashSet<string> deletes,
        bool clear,
        CancellationToken cancel)
    {
        try
        {
            await previousFlush.ConfigureAwait(false);
            await _db.SaveEconomyMarketQuotes(upserts, deletes, clear, cancel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Observe failures even when an operation completes after the bounded shutdown wait.
            Log.Error($"Failed to flush economy market quotes during shutdown: {e}");
        }
    }

    private void QueueForceFlush()
    {
        if (!_persist || _shuttingDown)
            return;

        if (_flushInProgress)
        {
            _forceFlushQueued = true;
            return;
        }

        StartFlush(forceAll: true);
    }

    private void MarkDirty(string marketKey)
    {
        _dirtyKeys.Add(marketKey);
        _deletedKeys.Remove(marketKey);
    }

    private void MarkDeleted(string marketKey)
    {
        _deletedKeys.Add(marketKey);
        _dirtyKeys.Remove(marketKey);
    }

    /// <summary>
    /// Wipe persisted market state: block any in-flight load apply, clear dirty tracking,
    /// and queue a full table clear + re-upsert of current in-memory quotes.
    /// Caller must already have cleared <see cref="_quotes"/>.
    /// </summary>
    private void ClearAllPersisted()
    {
        _blockLoadApply = true;
        _pendingFullClear = true;
        _dirtyKeys.Clear();
        _deletedKeys.Clear();

        if (_persist)
            QueueForceFlush();
    }

    private async Task LoadFromDatabaseAsync()
    {
        if (_loadStarted || !_persist || !_settings.Ready || _shuttingDown)
            return;

        _loadStarted = true;

        try
        {
            var rows = await _db.GetAllEconomyMarketQuotes(_databaseCancellation.Token).ConfigureAwait(false);
            _taskManager.RunOnMainThread(() => ApplyLoadedRows(rows));
        }
        catch (Exception e)
        {
            var error = e.ToString();
            _taskManager.RunOnMainThread(() => FinishLoadFailure(error));
        }
    }

    private void ApplyLoadedRows(
        IReadOnlyList<(string MarketKey, double Factor, float Trend, DateTime UpdatedAt)> rows)
    {
        if (_shuttingDown)
            return;

        if (!_persist)
        {
            _loadStarted = false;
            _loadCompleted = false;
            return;
        }

        _loadCompleted = true;

        // Admin reset while awaiting DB — do not repopulate from stale rows.
        if (_blockLoadApply)
        {
            Log.Info("Skipped applying economy market quotes load; market was reset during load.");
            return;
        }

        // Per-key merge only. Trades made while the query was in flight remain authoritative.
        var loaded = 0;
        foreach (var (key, factor, trend, _) in rows)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            // Older revisions persisted per-entity UIDs, which are meaningless after restart.
            if (key.StartsWith("uid:", StringComparison.Ordinal))
            {
                MarkDeleted(key);
                continue;
            }

            if (_dirtyKeys.Contains(key) || _deletedKeys.Contains(key) || _quotes.ContainsKey(key))
                continue;

            var sanitizedFactor = ClampFactor(factor);
            _quotes[key] = new MarketQuote(sanitizedFactor)
            {
                Trend = 0f,
                PreviousFactor = sanitizedFactor,
            };

            if (!factor.Equals(sanitizedFactor) || !trend.Equals(0f))
                MarkDirty(key);

            loaded++;
        }

        Log.Info($"Loaded {loaded} economy market quotes from database ({rows.Count} rows).");
    }

    private void FinishLoadFailure(string error)
    {
        if (_shuttingDown)
            return;

        _loadStarted = false;
        _loadCompleted = false;
        _retryAfter = _timing.CurTime + _persistInterval;
        Log.Error($"Failed to load economy market quotes; retrying after {_persistInterval}. {error}");
    }

    private void StartFlush(bool forceAll = false)
    {
        if (!_persist || !_settings.Ready || _shuttingDown || AdminQuotesBusy)
            return;

        // Keep dirty/deleted keys until the initial load has merged. Otherwise an early flush can
        // discard a tombstone and let stale loaded rows resurrect a quote that was already reset.
        if ((!_loadCompleted && !_blockLoadApply) || _timing.CurTime < _retryAfter)
        {
            _forceFlushQueued |= forceAll;
            return;
        }

        if (_flushInProgress)
        {
            if (forceAll || _pendingFullClear)
                _forceFlushQueued = true;
            return;
        }

        var doFullClear = _pendingFullClear;
        if (!forceAll && !doFullClear && _dirtyKeys.Count == 0 && _deletedKeys.Count == 0)
            return;

        _flushInProgress = true;
        _pendingFullClear = false;

        List<(string MarketKey, double Factor, float Trend)> upserts;
        List<string> deletes;

        if (doFullClear)
        {
            // Full table wipe then write whatever is currently in memory (usually empty after ResetAll).
            upserts = new List<(string, double, float)>(_quotes.Count);
            foreach (var (key, quote) in _quotes)
            {
                upserts.Add((key, quote.Factor, quote.Trend));
            }

            deletes = new List<string>();
            _dirtyKeys.Clear();
            _deletedKeys.Clear();
        }
        else if (forceAll)
        {
            upserts = new List<(string, double, float)>(_quotes.Count);
            foreach (var (key, quote) in _quotes)
            {
                upserts.Add((key, quote.Factor, quote.Trend));
            }

            deletes = new List<string>(_deletedKeys);
            _dirtyKeys.Clear();
            _deletedKeys.Clear();
        }
        else
        {
            upserts = new List<(string, double, float)>(_dirtyKeys.Count);
            foreach (var key in _dirtyKeys)
            {
                if (_quotes.TryGetValue(key, out var quote))
                    upserts.Add((key, quote.Factor, quote.Trend));
            }

            deletes = new List<string>(_deletedKeys);
            _dirtyKeys.Clear();
            _deletedKeys.Clear();
        }

        _flushingDeletes = deletes;
        _flushingFullClear = doFullClear;
        _flushTask = FlushSnapshotAsync(doFullClear, upserts, deletes);
    }

    private async Task FlushSnapshotAsync(
        bool doFullClear,
        List<(string MarketKey, double Factor, float Trend)> upserts,
        List<string> deletes)
    {
        string? error = null;

        try
        {
            await _db.SaveEconomyMarketQuotes(upserts, deletes, doFullClear, _databaseCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = e.ToString();
        }

        _taskManager.RunOnMainThread(() => FinishFlush(doFullClear, upserts, deletes, error));
    }

    private void FinishFlush(
        bool doFullClear,
        List<(string MarketKey, double Factor, float Trend)> upserts,
        List<string> deletes,
        string? error)
    {
        if (_shuttingDown)
            return;

        _flushInProgress = false;
        _flushingDeletes = Array.Empty<string>();
        _flushingFullClear = false;

        if (error != null)
        {
            Log.Error($"Failed to flush economy market quotes: {error}");

            if (doFullClear)
                _pendingFullClear = true;

            foreach (var (key, _, _) in upserts)
            {
                if (_quotes.ContainsKey(key))
                    _dirtyKeys.Add(key);
            }

            foreach (var key in deletes)
            {
                if (!_quotes.ContainsKey(key))
                    _deletedKeys.Add(key);
            }

            // A failed full clear remains pending, but must not recursively retry from its own callback.
            _retryAfter = _timing.CurTime + _persistInterval;
            return;
        }

        if (!_persist || (!_forceFlushQueued && !_pendingFullClear))
            return;

        _forceFlushQueued = false;
        StartFlush(forceAll: true);
    }
}
