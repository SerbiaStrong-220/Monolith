// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Working set for one buy/sell transaction so sequential lots share factor pressure
/// across multiple entities before optional commit to the global store.
/// </summary>
public sealed partial class MarketTransactionState
{
    public readonly Dictionary<string, double> Factors = new();

    public double GetOrLoad(string key, double fallback)
    {
        if (Factors.TryGetValue(key, out var value))
            return value;

        Factors[key] = fallback;
        return fallback;
    }

    public void Set(string key, double value)
    {
        Factors[key] = value;
    }
}

/// <summary>
/// Global supply/demand price index shared by every trade terminal on the server.
/// All buy/sell consoles read/write the same factor dictionary (and the same DB table when persistence is on).
/// Local console <c>MarketModifier</c> is applied on top by callers and is never stored here.
/// </summary>
public sealed partial class DynamicMarketSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private MarketCommodityGroupSystem _commodityGroups = default!;
    [Dependency] private MarketSettingsSystem _settings = default!;

    /// <summary>
    /// Single global quote store. Key format: "stack:&lt;id&gt;" or "proto:&lt;id&gt;".
    /// </summary>
    private readonly Dictionary<string, MarketQuote> _quotes = new();

    private bool _enabled = true;
    private double _minFactor = 0.01;
    private double _maxFactor = 9.99;
    private double _referenceVolume = 100;
    private TimeSpan _decayInterval = TimeSpan.FromSeconds(30);
    private double _decayRate = 0.0015;

    private TimeSpan _nextDecay;

    public bool Enabled => _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _settings.SettingsChanged += OnMarketSettingsChanged;
        OnMarketSettingsChanged();

        _nextDecay = _timing.CurTime + _decayInterval;
        InitializePersistence();
    }

    public override void Shutdown()
    {
        _settings.SettingsChanged -= OnMarketSettingsChanged;
        ShutdownPersistence();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        if (_settings.Ready && _enabled && _timing.CurTime >= _nextDecay)
        {
            _nextDecay += _decayInterval;
            RunMeanReversion();
        }

        UpdatePersistence();
    }

    private void OnMarketSettingsChanged()
    {
        var settings = _settings.Current;
        var interval = TimeSpan.FromSeconds(settings.DecayIntervalSeconds);
        if ((!_enabled && settings.Enabled) || interval != _decayInterval)
            _nextDecay = _timing.CurTime + interval;
        var changedBounds = _minFactor != settings.MinFactor || _maxFactor != settings.MaxFactor;
        _enabled = settings.Enabled;
        _minFactor = settings.MinFactor;
        _maxFactor = settings.MaxFactor;
        _referenceVolume = settings.ReferenceVolume;
        _decayInterval = interval;
        _decayRate = settings.DecayRate;
        if (changedBounds)
            ClampExistingQuotes();
    }

    private void ClampExistingQuotes()
    {
        foreach (var (key, quote) in _quotes)
        {
            var factor = ClampFactor(quote.Factor);
            if (factor.Equals(quote.Factor))
                continue;

            var updated = quote;
            updated.PreviousFactor = quote.Factor;
            updated.Trend = (float)(factor - quote.Factor);
            updated.Factor = factor;
            _quotes[key] = updated;
            MarkDirty(key);
        }
    }

    /// <summary>
    /// Resolve the global market key for a live entity.
    /// Stacks share one key by stack type; filled gas canisters use dominant gas:* key
    /// so they track Edison gas-sale prices.
    /// </summary>
    public string GetMarketKey(EntityUid uid, MetaDataComponent? meta = null)
    {
        if (TryComp<GasCanisterComponent>(uid, out var canister) &&
            TryGetDominantGasMarketKey(canister.Air) is { } liveGasKey)
        {
            return liveGasKey;
        }

        if (TryComp<GasTankComponent>(uid, out var tank) &&
            TryGetDominantGasMarketKey(tank.Air) is { } tankGasKey)
        {
            return tankGasKey;
        }

        if (TryComp<StackComponent>(uid, out var stack))
            return StackKey(stack.StackTypeId);

        meta ??= MetaData(uid);
        if (meta.EntityPrototype != null)
            return ProtoKey(meta.EntityPrototype.ID);

        return "unprototyped";
    }

    /// <summary>
    /// Resolve market key from an entity prototype id (cargo catalog / market stock).
    /// Filled gas canisters map to gas:* for correlation with Edison.
    /// </summary>
    public string GetMarketKeyFromPrototype(EntProtoId prototypeId)
    {
        if (_prototypes.TryIndex<EntityPrototype>(prototypeId, out var proto))
        {
            if (proto.TryGetComponent<GasCanisterComponent>(out var can, _factory) &&
                TryGetDominantGasMarketKey(can.Air) is { } gasKey)
            {
                return gasKey;
            }

            if (proto.TryGetComponent<GasTankComponent>(out var tank, _factory) &&
                TryGetDominantGasMarketKey(tank.Air) is { } tankGasKey)
            {
                return tankGasKey;
            }

            if (proto.TryGetComponent<StackComponent>(out var stack, _factory))
                return StackKey(stack.StackTypeId);
        }

        return ProtoKey(prototypeId.Id);
    }

    public static string StackKey(string stackTypeId) => $"stack:{stackTypeId}";

    public static string ProtoKey(string prototypeId) => $"proto:{prototypeId}";

    public double GetFactor(string marketKey)
    {
        if (!_enabled)
            return 1.0;

        return _quotes.TryGetValue(marketKey, out var quote) ? quote.Factor : ClampFactor(1.0);
    }

    public bool TryGetQuote(string marketKey, out MarketQuote quote)
    {
        if (_enabled && _quotes.TryGetValue(marketKey, out quote))
            return true;

        quote = new MarketQuote(_enabled ? ClampFactor(1.0) : 1.0);
        return false;
    }

    /// <summary>
    /// Preview an exact relative change without clipping it to market limits or mutating quotes.
    /// </summary>
    public bool TryGetScaledFactor(string marketKey, double multiplier, out double factor)
    {
        factor = 0;
        if (!_enabled || !double.IsFinite(multiplier) || multiplier <= 0)
            return false;

        var current = GetFactor(marketKey);
        var target = current * multiplier;
        if (!double.IsFinite(target) || target == current || target != ClampFactor(target))
            return false;

        factor = target;
        return true;
    }

    /// <summary>
    /// Set a factor directly for administration or an external market event.
    /// </summary>
    public void SetFactor(string marketKey, double factor)
    {
        factor = ClampFactor(factor);
        var quote = _quotes.GetValueOrDefault(marketKey, new MarketQuote(1.0));
        quote.PreviousFactor = quote.Factor;
        quote.Factor = factor;
        quote.Trend = (float)(factor - quote.PreviousFactor);
        _quotes[marketKey] = quote;
        MarkDirty(marketKey);
    }

    public void ResetAll()
    {
        _quotes.Clear();
        // Full DB table clear (not only in-memory keys) + block any in-flight load apply.
        ClearAllPersisted();
    }

    public void ResetKey(string marketKey)
    {
        _quotes.Remove(marketKey);
        MarkDeleted(marketKey);
    }

    /// <summary>
    /// Commit a finished transaction's working factors into the global store
    /// (after money has already been charged/paid using the same tx for pricing).
    /// </summary>
    public void CommitTransaction(MarketTransactionState tx)
    {
        if (!_enabled)
            return;

        foreach (var (key, factor) in tx.Factors)
        {
            CommitFactor(key, factor);
        }
    }

    public IReadOnlyDictionary<string, MarketQuote> GetAllQuotes() => _quotes;

    /// <summary>
    /// Lot size used for sequential pricing: stack max count, or 1 for non-stacks.
    /// </summary>
    public int GetLotSize(EntityUid uid)
    {
        if (!TryComp<StackComponent>(uid, out var stack))
            return 1;

        var max = _stack.GetMaxCount(stack);
        return max <= 0 || max == int.MaxValue ? Math.Max(1, stack.Count) : max;
    }

    public int GetLotSizeForPrototype(
        EntProtoId prototypeId,
        ProtoId<StackPrototype>? stackPrototypeId = null)
    {
        if (_prototypes.TryIndex<EntityPrototype>(prototypeId, out var proto) &&
            proto.TryGetComponent<StackComponent>(out var stack, _factory))
        {
            var max = _stack.GetMaxCount(stack);
            return max <= 0 || max == int.MaxValue ? Math.Max(1, stack.Count) : max;
        }

        if (stackPrototypeId != null && _prototypes.TryIndex(stackPrototypeId.Value, out var stackProto))
            return stackProto.MaxCount is > 0 and not int.MaxValue ? stackProto.MaxCount.Value : 30;

        return 1;
    }

    /// <summary>
    /// Number of market units spawned by one catalog product; resale stock already counts these units.
    /// </summary>
    public int GetUnitCountForPrototype(EntProtoId prototypeId)
    {
        if (_prototypes.TryIndex(prototypeId, out var prototype) &&
            prototype.TryGetComponent<StackComponent>(out var stack, _factory))
        {
            return Math.Max(1, stack.Count);
        }

        return 1;
    }

    /// <summary>
    /// Unit base price from a full entity appraisal and its stack count.
    /// </summary>
    public static double GetUnitBasePrice(double entityPrice, int unitCount)
    {
        if (unitCount <= 0)
            return entityPrice;

        return entityPrice / unitCount;
    }

    /// <summary>
    /// Round a monetary value without allowing a double-to-int overflow to become a negative payout.
    /// </summary>
    public static int RoundToPrice(double value, int minimum = 0)
    {
        minimum = Math.Max(0, minimum);
        if (double.IsNaN(value) || value <= minimum)
            return minimum;

        if (value >= int.MaxValue)
            return int.MaxValue;

        return Math.Max(minimum, (int)Math.Round(value));
    }

    /// <summary>
    /// Charge every fractional credit so splitting purchases cannot make them cheaper.
    /// </summary>
    public static int RoundBuyCost(double value, int minimum = 0)
    {
        return RoundToPrice(Math.Ceiling(value), minimum);
    }

    /// <summary>
    /// Pay only whole credits so splitting sales cannot create money from rounding.
    /// </summary>
    public static int RoundSellPayout(double value)
    {
        return RoundToPrice(Math.Floor(value));
    }

    /// <summary>
    /// Round a signed display value while saturating at the integer bounds.
    /// </summary>
    public static int RoundToInt(double value)
    {
        if (double.IsNaN(value))
            return 0;

        return (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Integrates the sell price over the traded volume, including the configured factor floor.
    /// The result is independent of stack splitting and transaction boundaries.
    /// <paramref name="lotSize"/> is retained and validated for caller compatibility.
    /// <paramref name="tx"/> carries factor state across entities in one pallet/cart action.
    /// When <paramref name="applyImpact"/> is true, the global quote store is updated (all terminals).
    /// <paramref name="consoleMod"/> is the local console MarketModifier (preserved; not global).
    /// </summary>
    public double CalculateSequentialSellValue(
        string marketKey,
        double unitBasePrice,
        double totalUnits,
        double lotSize,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact)
    {
        return ProcessLots(marketKey, unitBasePrice, totalUnits, lotSize, consoleMod, isSell: true, tx, applyImpact);
    }

    /// <summary>
    /// Integrates buy cost over the traded volume, including the configured factor ceiling.
    /// </summary>
    public double CalculateSequentialBuyCost(
        string marketKey,
        double unitBasePrice,
        double totalUnits,
        double lotSize,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact)
    {
        return ProcessLots(marketKey, unitBasePrice, totalUnits, lotSize, consoleMod, isSell: false, tx, applyImpact);
    }

    /// <summary>
    /// Convenience: sell pricing for a single entity already on a pallet.
    /// Gas contents use gas:* keys shared with Edison; container shells use their own prototype quote.
    /// </summary>
    public double CalculateEntitySellValue(
        EntityUid uid,
        double entityBasePrice,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact,
        MetaDataComponent? meta = null)
    {
        if (!double.IsFinite(entityBasePrice) || !double.IsFinite(consoleMod) || consoleMod <= 0)
            return 0;

        if (!_enabled || entityBasePrice <= 0)
            return entityBasePrice * consoleMod;

        // Preserve the supplied shell appraisal, including discounts, while repricing its gas contents.
        if (TryComp<GasCanisterComponent>(uid, out var canister))
            return CalculateGasContainerSellValue(uid, canister.Air, consoleMod, tx, applyImpact, usePurity: true,
                shellBasePrice: Math.Max(0, entityBasePrice - _atmos.GetPrice(canister.Air)));

        if (TryComp<GasTankComponent>(uid, out var tank))
            return CalculateGasContainerSellValue(uid, tank.Air, consoleMod, tx, applyImpact, usePurity: true,
                shellBasePrice: Math.Max(0, entityBasePrice - _atmos.GetPrice(tank.Air)));

        var units = 1;
        if (TryComp<StackComponent>(uid, out var stack))
            units = Math.Max(1, stack.Count);

        var unitPrice = GetUnitBasePrice(entityBasePrice, units);
        var lotSize = GetLotSize(uid);
        var key = GetMarketKey(uid, meta);

        return CalculateSequentialSellValue(key, unitPrice, units, lotSize, consoleMod, tx, applyImpact);
    }

    private double ProcessLots(
        string marketKey,
        double unitBasePrice,
        double totalUnits,
        double lotSize,
        double consoleMod,
        bool isSell,
        MarketTransactionState? tx,
        bool applyImpact)
    {
        if (!double.IsFinite(totalUnits) ||
            !double.IsFinite(unitBasePrice) ||
            !double.IsFinite(lotSize) ||
            !double.IsFinite(consoleMod) ||
            totalUnits <= 0 ||
            unitBasePrice <= 0 ||
            lotSize <= 0 ||
            consoleMod <= 0)
        {
            return 0;
        }

        if (!_enabled)
            return unitBasePrice * totalUnits * consoleMod;

        tx ??= new MarketTransactionState();

        var workingFactor = ClampFactor(tx.GetOrLoad(marketKey, GetFactor(marketKey)));
        var integratedFactor = workingFactor * totalUnits;
        // A purchase and its reverse sale must follow the same curve. Independent strengths let
        // sell/buy cycles profit even when each individual purchase exceeds its immediate resale.
        var rate = _settings.GetImpactStrength(_commodityGroups.GetGroup(marketKey)) / _referenceVolume;
        if (rate > 0)
        {
            // Integrate f(u) = current * exp(±rate * u) until the limit, then use the limit price.
            // Midpoint estimates allow profit by buying a stack and selling it in smaller pieces.
            var limit = isSell ? Math.Min(_minFactor, _maxFactor) : Math.Max(_minFactor, _maxFactor);
            var distance = isSell ? Math.Log(workingFactor / limit) : Math.Log(limit / workingFactor);
            var unitsToLimit = distance / rate;
            var changingUnits = Math.Min(totalUnits, unitsToLimit);
            var exponent = (isSell ? -rate : rate) * changingUnits;
            // ExpM1 avoids cancellation for tiny impacts; a zero exponent is the flat-price limit.
            var averageFactor = exponent == 0
                ? workingFactor
                : workingFactor * (double.ExpM1(exponent) / exponent);
            integratedFactor = averageFactor * changingUnits + limit * Math.Max(0, totalUnits - changingUnits);
            workingFactor = totalUnits >= unitsToLimit
                ? limit
                : ClampFactor(workingFactor * Math.Exp(exponent));
        }

        tx.Set(marketKey, workingFactor);

        if (applyImpact)
            CommitFactor(marketKey, workingFactor);

        return unitBasePrice * consoleMod * integratedFactor;
    }

    private void CommitFactor(string marketKey, double newFactor)
    {
        newFactor = ClampFactor(newFactor);
        var quote = _quotes.GetValueOrDefault(marketKey, new MarketQuote(1.0));
        quote.PreviousFactor = quote.Factor;
        quote.Trend = (float)(newFactor - quote.Factor);
        quote.Factor = newFactor;
        _quotes[marketKey] = quote;
        MarkDirty(marketKey);
    }

    private double ClampFactor(double factor)
    {
        var min = Math.Min(_minFactor, _maxFactor);
        var max = Math.Max(_minFactor, _maxFactor);
        if (!double.IsFinite(factor))
            return Math.Clamp(1.0, min, max);

        return Math.Clamp(factor, min, max);
    }

    private static double SanitizeIntervalSeconds(float value, double fallback)
    {
        return float.IsFinite(value) && value >= 0.001f && value <= TimeSpan.MaxValue.TotalSeconds / 2
            ? value
            : fallback;
    }

    private void RunMeanReversion()
    {
        if (_quotes.Count == 0 || _decayRate <= 0f)
            return;

        List<string>? toRemove = null;

        foreach (var (key, quote) in _quotes)
        {
            var next = quote.Factor + (1.0 - quote.Factor) * _decayRate;
            next = ClampFactor(next);

            if (Math.Abs(next - 1.0) < 0.0005)
            {
                toRemove ??= new List<string>();
                toRemove.Add(key);
                continue;
            }

            var updated = quote;
            updated.PreviousFactor = quote.Factor;
            updated.Trend = (float)(next - quote.Factor);
            updated.Factor = next;
            _quotes[key] = updated;
            MarkDirty(key);
        }

        if (toRemove == null)
            return;

        foreach (var key in toRemove)
        {
            _quotes.Remove(key);
            MarkDeleted(key);
        }
    }
}
