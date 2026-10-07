using System.Globalization;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Administration;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Stacks;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Economy.Admin;

/// <summary>
/// Permission-checked economy administration. Reads cached prototype classifications only;
/// persistence completion is the sole point at which a mutation is reported as successful.
/// </summary>
public sealed partial class MarketAdminSystem : EntitySystem
{
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 128;
    private const int MaxKeyLength = 256;

    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private DynamicMarketSystem _market = default!;
    [Dependency] private MarketSettingsSystem _settings = default!;
    [Dependency] private MarketCommodityGroupSystem _groups = default!;

    private sealed record CatalogEntry(string Name, MarketCommodityClassification Classification, string SearchText);

    private IReadOnlyDictionary<string, MarketCommodityClassification>? _classificationSource;
    private readonly Dictionary<string, CatalogEntry> _catalog = new(StringComparer.Ordinal);

    public event Action? QuotesChanged;

    public bool CanAdminister(ICommonSession player)
    {
        return _admins.HasAdminFlag(player, AdminFlags.EconomyDB);
    }

    public bool TryOpen(ICommonSession? player, EuiMessageBase? initialAction = null)
    {
        if (player == null || !CanAdminister(player))
            return false;

        var window = new MarketAdminEui(this, _settings, _admins, _timing);
        _eui.OpenEui(window, player);
        if (initialAction != null)
            window.HandleMessage(initialAction);
        return true;
    }

    public bool IsValidQuery(MarketAdminQueryMessage query)
    {
        return query.Search != null && query.Search.Length <= MaxSearchLength && query.Page >= 0 &&
               query.PageSize is > 0 and <= MaxPageSize &&
               (query.Group == null || IsKnownGroup(query.Group.Value));
    }

    public MarketAdminState GetState(
        ICommonSession player,
        string search = "",
        ProtoId<MarketCommodityGroupPrototype>? group = null,
        int page = 0,
        int pageSize = 50)
    {
        if (!CanAdminister(player) || !IsValidQuery(new MarketAdminQueryMessage(search, null, page, pageSize)))
            return new MarketAdminState();

        // A prototype reload can remove the group selected by an already-open window.
        if (group != null && !IsKnownGroup(group.Value))
            group = null;

        var snapshot = _settings.GetSnapshot();
        var state = new MarketAdminState
        {
            Settings = snapshot,
            Ready = _settings.Ready,
            Saving = _settings.Saving || _market.AdminQuotesBusy,
            QuotesPersistenceEnabled = _market.QuotesPersistenceEnabled,
            Search = search,
            GroupFilter = group,
            PageSize = pageSize,
        };
        foreach (var prototype in _prototypes.EnumeratePrototypes<MarketCommodityGroupPrototype>())
        {
            ProtoId<MarketCommodityGroupPrototype> id = prototype.ID;
            double? overridden = snapshot.GroupOverrides.TryGetValue(id, out var value) ? value : null;
            state.Groups.Add(new MarketAdminGroup(id, prototype.Name, prototype.Gases, prototype.ImpactMultiplier,
                snapshot.Global.ImpactStrength * prototype.ImpactMultiplier,
                _settings.GetImpactStrength(id), overridden));
        }
        state.Groups.Sort((left, right) => string.Compare(left.Id.Id, right.Id.Id, StringComparison.Ordinal));

        EnsureCatalog();
        var keys = new HashSet<string>(_catalog.Keys, StringComparer.Ordinal);
        var quotes = _market.GetAllQuotes();
        state.ResetQuoteCount = quotes.Count;
        keys.UnionWith(quotes.Keys);
        var matching = new List<string>();
        foreach (var key in keys)
        {
            var entry = _catalog.GetValueOrDefault(key);
            var classification = entry?.Classification ?? GetClassification(key);
            if (group != null && classification.Group != group.Value)
                continue;
            if (search.Length != 0 && !key.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(entry?.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            matching.Add(key);
        }
        matching.Sort(StringComparer.Ordinal);
        state.TotalQuotes = matching.Count;
        state.Page = Math.Min(page, Math.Max(0, (matching.Count - 1) / pageSize));
        var first = state.Page * pageSize;
        var last = Math.Min(first + pageSize, matching.Count);
        for (var i = first; i < last; i++)
        {
            var key = matching[i];
            var entry = _catalog.GetValueOrDefault(key);
            var classification = entry?.Classification ?? GetClassification(key);
            var factor = 1.0;
            var trend = 0f;
            if (quotes.TryGetValue(key, out var quote))
            {
                factor = quote.Factor;
                trend = quote.Trend;
            }
            state.Quotes.Add(new MarketAdminQuote(key, entry?.Name ?? key,
                classification.Group, classification.Rule, factor, trend));
        }
        return state;
    }

    public void ApplySettings(ICommonSession player, MarketAdminApplySettingsMessage message, Action<bool, string?> completed)
    {
        if (!CanAdminister(player))
        {
            completed(false, "economy-admin-error-permission");
            return;
        }

        var before = _settings.GetSnapshot();
        if (message.Globals == null || message.GroupOverrides == null || _market.AdminQuotesBusy)
        {
            completed(false, "economy-admin-error-busy");
            return;
        }

        var oldGroups = DescribeGroups(before.GroupOverrides);
        var newGroups = DescribeGroups(message.GroupOverrides);
        // Rejected drafts may contain NaN/Infinity, which must not enter the log's JSON serializer.
        var oldGlobals = before.Overrides.ToString();
        var newGlobals = message.Globals.ToString();
        _settings.Apply(message.Revision, message.Globals, message.GroupOverrides, (success, failure) =>
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.High,
                $"Economy settings by {player.UserId}: expected revision {message.Revision}; global overrides {oldGlobals} -> {newGlobals}; group overrides [{oldGroups}] -> [{newGroups}]; saved={success}; error={failure}");
            completed(success, success ? "economy-admin-saved" : failure);
        });
    }

    public bool CanMutateQuotes(ICommonSession? player, out string? failure)
    {
        // Manual mutations require a player session and the economy window's confirmation flow.
        failure = player == null || !CanAdminister(player) ? "economy-admin-error-permission"
            : !_settings.Ready ? "economy-admin-error-unavailable"
            : _settings.Saving || _market.AdminQuotesBusy ? "economy-admin-error-busy"
            : !_market.QuotesPersistenceEnabled ? "economy-admin-error-persistence-disabled"
            : !_market.AdminQuotesReady ? "economy-admin-error-unavailable"
            : null;
        return failure == null;
    }

    public bool CanMutateSettings(ICommonSession player, out string? failure)
    {
        failure = !CanAdminister(player) ? "economy-admin-error-permission"
            : !_settings.Ready ? "economy-admin-error-unavailable"
            : _settings.Saving || _market.AdminQuotesBusy ? "economy-admin-error-busy"
            : null;
        return failure == null;
    }

    public void SetQuote(ICommonSession? player, string key, double factor, Action<bool, string?> completed)
    {
        if (!ValidateQuoteMutation(player, key, completed))
            return;
        var limits = _settings.GetSnapshot().Global;
        if (!double.IsFinite(factor) || factor < limits.MinFactor || factor > limits.MaxFactor)
        {
            completed(false, "economy-admin-error-factor");
            return;
        }

        var before = _market.GetAllQuotes().TryGetValue(key, out var quote) ? quote.Factor : 1.0;
        var actor = player?.UserId.ToString() ?? "server-console";
        _market.ApplyAdminQuotes(new Dictionary<string, double> { [key] = factor }, null, false, (success, failure) =>
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.High,
                $"Economy quote by {actor}: key={key}; factor={before} -> {factor}; saved={success}; error={failure}");
            CompleteQuotes(success, failure, completed);
        });
    }

    public void ResetQuote(ICommonSession? player, string key, Action<bool, string?> completed)
    {
        if (!ValidateQuoteMutation(player, key, completed))
            return;
        var before = _market.GetAllQuotes().TryGetValue(key, out var quote) ? quote.Factor : 1.0;
        var actor = player?.UserId.ToString() ?? "server-console";
        _market.ApplyAdminQuotes(null, new[] { key }, false, (success, failure) =>
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.High,
                $"Economy quote reset by {actor}: key={key}; factor={before} -> 1; saved={success}; error={failure}");
            CompleteQuotes(success, failure, completed);
        });
    }

    public void ResetGroup(ICommonSession player, ProtoId<MarketCommodityGroupPrototype> group, Action<bool, string?> completed)
    {
        if (!CanMutateQuotes(player, out var failure))
        {
            completed(false, failure);
            return;
        }
        if (!IsKnownGroup(group))
        {
            completed(false, "economy-admin-error-group");
            return;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        EnsureCatalog();
        foreach (var (key, entry) in _catalog)
        {
            if (entry.Classification.Group == group)
                keys.Add(key);
        }
        foreach (var key in _market.GetAllQuotes().Keys)
        {
            if (_groups.GetGroup(key) == group)
                keys.Add(key);
        }

        if (keys.Count == 0)
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.Low,
                $"Economy group quotes reset by {player.UserId}: group={group.Id}; keys=0; no changes required; saved=true");
            completed(true, "economy-admin-quotes-saved");
            return;
        }

        var before = DescribeQuotes(keys);
        _market.ApplyAdminQuotes(null, keys, false, (success, error) =>
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.High,
                $"Economy group quotes reset by {player.UserId}: group={group.Id}; keys={keys.Count}; [{before}] -> base; saved={success}; error={error}");
            CompleteQuotes(success, error, completed);
        });
    }

    internal void ResetAllQuotes(ICommonSession player, Action<bool, string?> completed)
    {
        if (!CanMutateQuotes(player, out var failure))
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.High,
                $"Economy ALL quotes reset by {player.UserId}: saved=false; error={failure}");
            completed(false, failure);
            return;
        }

        var count = _market.GetAllQuotes().Count;
        var operation = Guid.NewGuid();
        _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme,
            $"Economy ALL quotes reset requested by {player.UserId}: operation={operation}; three confirmations accepted; {count} in-memory deviations and ALL persisted market quotes targeted; persistence pending");
        _market.ApplyAdminQuotes(null, null, true, (success, error) =>
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme,
                $"Economy ALL quotes reset by {player.UserId}: operation={operation}; {count} in-memory deviations and ALL persisted market quotes -> base; settings and unrelated database tables unchanged; saved={success}; error={error}");
            CompleteQuotes(success, error, completed);
        });
    }

    private bool ValidateQuoteMutation(ICommonSession? player, string key, Action<bool, string?> completed)
    {
        if (!CanMutateQuotes(player, out var failure))
        {
            completed(false, failure);
            return false;
        }
        EnsureCatalog();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength ||
            !_catalog.ContainsKey(key) && !_market.GetAllQuotes().ContainsKey(key))
        {
            completed(false, "economy-admin-error-key");
            return false;
        }
        return true;
    }

    private void CompleteQuotes(bool success, string? failure, Action<bool, string?> completed)
    {
        // Refresh other open admin windows only for administrative operations, never per trade.
        QuotesChanged?.Invoke();
        completed(success, success ? "economy-admin-quotes-saved" : failure);
    }

    private MarketCommodityClassification GetClassification(string key)
    {
        return _groups.TryGetClassification(key, out var classification)
            ? classification
            : new MarketCommodityClassification(_groups.GetGroup(key));
    }

    private bool IsKnownGroup(ProtoId<MarketCommodityGroupPrototype> group)
    {
        return !string.IsNullOrWhiteSpace(group.Id) && group.Id.Length <= MaxKeyLength && _prototypes.HasIndex(group);
    }

    private void EnsureCatalog()
    {
        var classifications = _groups.GetClassifications();
        if (ReferenceEquals(classifications, _classificationSource))
            return;
        _classificationSource = classifications;
        _catalog.Clear();

        foreach (var (key, classification) in classifications)
        {
            var name = key;
            if (key.StartsWith("proto:", StringComparison.Ordinal) &&
                _prototypes.TryIndex(new EntProtoId(key[6..]), out var prototype))
            {
                // Stack variants are aliases of the canonical stack commodity, not extra rows.
                if (prototype.Abstract || prototype.TryGetComponent<StackComponent>(out _, _factory))
                    continue;
                name = prototype.Name;
            }
            else if (key.StartsWith("stack:", StringComparison.Ordinal) &&
                     _prototypes.TryIndex(new ProtoId<StackPrototype>(key[6..]), out var stack))
            {
                name = Loc.GetString(stack.Name);
            }
            else if (key.StartsWith("gas:", StringComparison.Ordinal) &&
                     _prototypes.TryIndex(new ProtoId<GasPrototype>(key[4..]), out var gas))
            {
                name = Loc.GetString(gas.Name);
            }
            _catalog.Add(key, new CatalogEntry(name, classification, name));
        }

        foreach (var (prototypeId, _) in _groups.GetPrototypeClassifications())
        {
            if (!_prototypes.TryIndex(prototypeId, out var prototype) ||
                !prototype.TryGetComponent<StackComponent>(out var stack, _factory))
                continue;
            var key = DynamicMarketSystem.StackKey(stack.StackTypeId);
            if (_catalog.TryGetValue(key, out var entry))
                _catalog[key] = entry with { SearchText = $"{entry.SearchText}\n{prototypeId.Id}\n{prototype.Name}" };
        }
    }

    private static string DescribeGroups(Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> groups)
    {
        var text = new StringBuilder();
        foreach (var (id, value) in groups)
            text.Append(id.Id).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        return text.ToString();
    }

    private string DescribeQuotes(IEnumerable<string> keys)
    {
        var text = new StringBuilder();
        var quotes = _market.GetAllQuotes();
        foreach (var key in keys)
        {
            if (quotes.TryGetValue(key, out var quote))
                text.Append(key).Append('=').Append(quote.Factor.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }
        return text.ToString();
    }
}
