using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketSettingsSystem
{
    private sealed record SettingsDocument(
        int Version,
        MarketGlobalSettingsOverride Global,
        Dictionary<string, double> Groups);

    private readonly CancellationTokenSource _cancellation = new();
    private bool _loading;
    private bool _stopping;
    private long _databaseRevision;
    private TimeSpan _retryAt;

    public void RefreshLoad()
    {
        if (_loading || Saving || Ready || _stopping)
            return;
        _loading = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        (long Revision, string Settings)? row = null;
        string? error = null;
        try
        {
            row = await _database.GetEconomyMarketSettings(_cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        _tasks.RunOnMainThread(() => FinishLoad(row, error));
    }

    private void FinishLoad((long Revision, string Settings)? row, string? error)
    {
        if (_stopping)
            return;
        _loading = false;
        if (error == null)
        {
            try
            {
                var globals = new MarketGlobalSettingsOverride();
                var groups = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double>();
                if (row is { } saved)
                {
                    var document = JsonSerializer.Deserialize<SettingsDocument>(saved.Settings);
                    if (document is not { Version: 1, Global: not null, Groups: not null })
                        throw new InvalidOperationException("Unknown or invalid market settings document.");
                    globals = document.Global;
                    foreach (var (id, strength) in document.Groups)
                        groups.Add(id, strength);
                    if (!Validate(globals, groups, loading: true))
                        throw new InvalidOperationException("Invalid persisted market configuration.");
                    _databaseRevision = saved.Revision;
                }
                _overrides = globals;
                _groupOverrides = groups;
                foreach (var group in groups.Keys)
                {
                    if (!_prototypes.HasIndex(group))
                        Log.Warning($"Saved market group {group} is unavailable; keeping its override inactive.");
                }
                Ready = true;
                LastError = null;
                Rebuild();
                return;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
            }
        }
        LastError = "economy-admin-error-load";
        _retryAt = _timing.CurTime + TimeSpan.FromSeconds(30);
        Log.Error($"Could not load market settings; retrying in 30 seconds. {error}");
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Validate and persist one complete override document. Callback success means saved and applied.
    /// </summary>
    public void Apply(
        long expectedRevision,
        MarketGlobalSettingsOverride globals,
        Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> groupOverrides,
        Action<bool, string?> completed)
    {
        if (!Ready || Saving || _stopping)
        {
            completed(false, "economy-admin-error-busy");
            return;
        }
        if (expectedRevision != _revision)
        {
            completed(false, "economy-admin-error-conflict");
            return;
        }
        if (globals == null || groupOverrides == null || !Validate(globals, groupOverrides))
        {
            completed(false, "economy-admin-error-invalid");
            return;
        }

        var groups = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double>(groupOverrides);
        var storedGroups = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (group, strength) in groups)
            storedGroups.Add(group.Id, strength);
        var json = JsonSerializer.Serialize(new SettingsDocument(1, globals, storedGroups));
        Saving = true;
        SettingsChanged?.Invoke();
        _ = SaveAsync(json, globals, groups, completed);
    }

    private async Task SaveAsync(
        string json,
        MarketGlobalSettingsOverride globals,
        Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> groups,
        Action<bool, string?> completed)
    {
        var saved = false;
        string? error = null;
        try
        {
            saved = await _database.TrySaveEconomyMarketSettings(_databaseRevision, json, _cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        _tasks.RunOnMainThread(() =>
        {
            if (_stopping)
                return;
            Saving = false;
            if (!saved)
            {
                LastError = error == null ? "economy-admin-error-conflict" : "economy-admin-error-save";
                if (error != null)
                    Log.Error($"Could not save market settings: {error}");
                else
                {
                    // Another server/operator changed the stored revision; reload before accepting a retry.
                    Ready = false;
                    RefreshLoad();
                }
                SettingsChanged?.Invoke();
                completed(false, LastError);
                return;
            }
            _databaseRevision++;
            _overrides = globals;
            _groupOverrides = groups;
            LastError = null;
            Rebuild();
            completed(true, null);
        });
    }
}
