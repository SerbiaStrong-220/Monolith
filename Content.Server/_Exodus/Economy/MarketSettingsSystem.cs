using Content.Server.Database;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Persistent administrative overrides for the global market. Neither prototypes nor CVars are mutated.
/// </summary>
public sealed partial class MarketSettingsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerDbManager _database = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IGameTiming _timing = default!;

    private MarketGlobalSettings _defaults = new();
    private MarketGlobalSettingsOverride _overrides = new();
    private Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> _groupOverrides = new();
    private readonly Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> _strengths = new();
    private long _revision;
    private bool _initializing;

    public MarketGlobalSettings Current { get; private set; } = new();
    public bool Ready { get; private set; }
    public bool Saving { get; private set; }
    public string? LastError { get; private set; }
    public event Action? SettingsChanged;

    public override void Initialize()
    {
        base.Initialize();
        _initializing = true;
        Subs.CVar(_configuration, EXCVars.DynamicMarketEnabled, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketMinFactor, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketMaxFactor, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketSellImpact, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketBuyImpact, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketReferenceVolume, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketDecayIntervalSeconds, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.DynamicMarketDecayRate, _ => ConfigurationChanged());
        Subs.CVar(_configuration, EXCVars.MarketPurchaseMargin, _ => ConfigurationChanged());
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        _initializing = false;
        ConfigurationChanged();
        RefreshLoad();
    }

    public override void Shutdown()
    {
        _stopping = true;
        _cancellation.Cancel();
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        SettingsChanged = null;
        _cancellation.Dispose();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        if (!Ready && !_loading && !_stopping && _timing.CurTime >= _retryAt)
            RefreshLoad();
    }

    public MarketSettingsSnapshot GetSnapshot()
    {
        return new MarketSettingsSnapshot(_revision, _defaults, Current, _overrides, new(_groupOverrides));
    }

    public double GetImpactStrength(ProtoId<MarketCommodityGroupPrototype> group)
    {
        return _strengths.GetValueOrDefault(group, Current.ImpactStrength);
    }

    private void ConfigurationChanged()
    {
        if (_initializing)
            return;

        var min = Positive(_configuration.GetCVar(EXCVars.DynamicMarketMinFactor), 0.01);
        var max = Positive(_configuration.GetCVar(EXCVars.DynamicMarketMaxFactor), 9.99);
        _defaults = new MarketGlobalSettings(
            _configuration.GetCVar(EXCVars.DynamicMarketEnabled),
            Math.Max(NonNegative(_configuration.GetCVar(EXCVars.DynamicMarketBuyImpact), 0.08),
                NonNegative(_configuration.GetCVar(EXCVars.DynamicMarketSellImpact), 0.08)),
            Positive(_configuration.GetCVar(EXCVars.DynamicMarketReferenceVolume), 100),
            Math.Min(min, max),
            Math.Max(min, max),
            Interval(_configuration.GetCVar(EXCVars.DynamicMarketDecayIntervalSeconds)),
            Fraction(_configuration.GetCVar(EXCVars.DynamicMarketDecayRate), 0.0015),
            Fraction(_configuration.GetCVar(EXCVars.MarketPurchaseMargin), 0.05));
        Rebuild();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<MarketCommodityGroupPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        Current = _overrides.Resolve(_defaults);
        // Legacy console configuration permits either bound ordering. Administrative writes are stricter.
        if (Current.MinFactor > Current.MaxFactor)
            Current = Current with { MinFactor = Current.MaxFactor, MaxFactor = Current.MinFactor };

        _strengths.Clear();
        foreach (var group in _prototypes.EnumeratePrototypes<MarketCommodityGroupPrototype>())
        {
            var strength = _groupOverrides.GetValueOrDefault(group.ID, Current.ImpactStrength * group.ImpactMultiplier);
            if (double.IsFinite(strength) && strength >= 0)
                _strengths[group.ID] = strength;
        }
        _revision++;
        SettingsChanged?.Invoke();
    }

    private bool Validate(
        MarketGlobalSettingsOverride globals,
        Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> groups,
        bool loading = false)
    {
        var effective = globals.Resolve(_defaults);
        if (!effective.IsValid() || groups.Count > 1024)
            return false;

        foreach (var (group, strength) in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || group.Id.Length > 256 ||
                !double.IsFinite(strength) || strength < 0 ||
                !double.IsFinite(strength / effective.ReferenceVolume))
                return false;

            // Keep obsolete persisted IDs inert so a content update does not silently alter the saved document.
            if (!_prototypes.HasIndex(group) && !loading && !_groupOverrides.ContainsKey(group))
                return false;
        }
        return true;
    }

    private static double Positive(double value, double fallback) => double.IsFinite(value) && value > 0 ? value : fallback;
    private static double NonNegative(double value, double fallback) => double.IsFinite(value) && value >= 0 ? value : fallback;
    private static double Fraction(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    private static double Interval(double value) => double.IsFinite(value) && value >= 0.001 && value <= TimeSpan.MaxValue.TotalSeconds / 2 ? value : 30;
}
