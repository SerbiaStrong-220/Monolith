// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Systems;
using Content.Shared.Atmos;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Resolves nominal delivery and sale commodities without spawning entities for catalog previews.
/// Prototype baskets are cached until a prototype reload. Live baskets always inspect current contents.
/// </summary>
public sealed partial class MarketBasketSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private DefaultItemPriceSystem _fallback = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;

    private const int MaxDepth = 32;
    private const int MaxVisitedNodes = 8192;
    private const int MaxLines = 4096;
    private const double MaxQuantity = 1e12;

    private readonly Dictionary<EntProtoId, (MarketBasket Basket, string? Failure)> _cache = new();
    private static readonly MarketBasket EmptyBasket = new(Array.Empty<MarketBasketLine>(), 1, false);

    private sealed class BuildState
    {
        public readonly List<MarketBasketLine> Lines = new();
        public List<MarketBasketPackage>? Packages;
        public readonly HashSet<EntProtoId> Prototypes = new();
        public readonly HashSet<EntityUid> Entities = new();
        public readonly HashSet<string> Tables = new();
        public int Visited;
        public double NominalValue;
        public bool Exact = true;
        public string? Failure;

        public bool Fail(string reason)
        {
            Failure ??= reason;
            return false;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        RebuildStackTaxMultipliers();
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        // Material, reagent, recipe, table and entity changes can all affect a nested basket.
        _cache.Clear();
        if (args.WasModified<EntityPrototype>() || args.WasModified<StackPrototype>())
            RebuildStackTaxMultipliers();
    }

    public bool TryGetPrototypeBasket(EntProtoId prototypeId, out MarketBasket basket, out string? failure)
    {
        if (_cache.TryGetValue(prototypeId, out var cached))
        {
            (basket, failure) = cached;
            return failure == null;
        }

        var state = new BuildState();
        var units = 1;
        if (_prototypes.TryIndex(prototypeId, out var prototype) &&
            prototype.TryGetComponent<StackComponent>(out var stack, _factory))
        {
            units = stack.Count;
        }

        if (units <= 0 || !AddPrototype(prototypeId, 1, state, 0))
        {
            basket = EmptyBasket;
            failure = state.Failure ?? $"Invalid spawned stack count for {prototypeId}.";
        }
        else
        {
            basket = new MarketBasket(state.Lines, units, state.Exact, state.Packages);
            failure = null;
        }

        _cache[prototypeId] = (basket, failure);
        return failure == null;
    }

    public bool TryGetEntityBasket(
        EntityUid uid,
        out MarketBasket basket,
        out string? failure,
        EntityUid? appraisalGrid = null)
    {
        var state = new BuildState();
        if (!AddEntity(uid, state, 0, appraisalGrid))
        {
            basket = EmptyBasket;
            failure = state.Failure;
            return false;
        }

        var units = TryComp<StackComponent>(uid, out var stack) ? Math.Max(1, stack.Count) : 1;
        basket = new MarketBasket(state.Lines, units, state.Exact);
        failure = null;
        return true;
    }

    /// <summary>
    /// Resolves only this entity's live commodities, including gas, stored materials and virtual ammunition.
    /// Actual contained entities are left to the caller's traversal. When an opaque appraisal or unopened
    /// package owns the whole subtree, <paramref name="includesContents"/> prevents counting it again.
    /// Inexact prototype payload bounds are rejected rather than treated as actual sold quantities.
    /// </summary>
    public bool TryGetEntityOwnBasket(
        EntityUid uid,
        out MarketBasket basket,
        out bool includesContents,
        out string? failure)
    {
        var state = new BuildState();
        if (!AddEntityOwn(uid, state, 0, null, out includesContents) || !state.Exact)
        {
            basket = EmptyBasket;
            includesContents = false;
            failure = state.Failure ?? "Inexact prototype payload cannot describe live market quantities.";
            return false;
        }

        var units = TryComp<StackComponent>(uid, out var stack) ? Math.Max(1, stack.Count) : 1;
        basket = new MarketBasket(state.Lines, units, true);
        failure = null;
        return true;
    }

    private static bool Visit(BuildState state, int depth)
    {
        return depth <= MaxDepth && ++state.Visited <= MaxVisitedNodes ||
               state.Fail("Market basket exceeds the bounded traversal limit.");
    }

    private static bool AddLine(BuildState state, MarketBasketLine line)
    {
        if (!double.IsFinite(line.UnitBasePrice) || !double.IsFinite(line.Quantity) ||
            line.UnitBasePrice < 0 || line.Quantity < 0 || line.Quantity > MaxQuantity ||
            !double.IsFinite(line.UnitBasePrice * line.Quantity))
        {
            return state.Fail($"Invalid market basket quantity or price for {line.PrototypeId}.");
        }

        if (line.Quantity == 0)
            return true;

        if (state.Lines.Count >= MaxLines)
            return state.Fail("Market basket contains too many commodity lines.");

        var total = state.NominalValue + line.UnitBasePrice * line.Quantity;
        if (!double.IsFinite(total))
            return state.Fail("Market basket nominal value exceeds the finite price range.");

        state.Lines.Add(line);
        state.NominalValue = total;
        return true;
    }

    private bool AddGas(EntProtoId prototype, GasMixture air, double count, double purity, bool ignoreModifier,
        BuildState state, EntityUid? sourceEntity = null)
    {
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = air.GetMoles(i);
            if (!float.IsFinite(moles) || moles < 0)
                return state.Fail($"Invalid gas mixture in {prototype}.");

            if (moles == 0)
                continue;

            // Gas is transferable between tanks; only the shell keeps the container's item tax.
            if (!AddLine(state, new MarketBasketLine(prototype, DynamicMarketSystem.GasKey(i),
                    _atmos.GetGas(i).PricePerMole * purity, moles * count, ignoreModifier, sourceEntity)))
            {
                return false;
            }
        }

        return true;
    }
}
