// (c) Space Exodus Team - EXDS-RL with CLA
// Exodus: shared inventory operations for cargo orders and market consoles.
using System.Diagnostics.CodeAnalysis;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared._NF.Market.BUI;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Market.Systems;

public sealed partial class MarketSystem
{
    [Dependency] private MarketPurchaseSystem _marketPurchases = default!;
    [Dependency] private MarketSellCeilingSystem _marketSellCeilings = default!;
    [Dependency] private MarketInventorySystem _marketInventory = default!;

    public bool TryGetStock(EntityUid station, EntProtoId prototype, [NotNullWhen(true)] out MarketData? stock)
    {
        stock = null;
        return _marketInventory.TryGetStock(prototype, out stock);
    }

    /// <summary>
    /// Reserves units before payment. The returned snapshot can be returned without a monetary refund if payment fails.
    /// </summary>
    public bool TryTakeStock(EntityUid station, EntProtoId prototype, int amount, [NotNullWhen(true)] out MarketData? reserved)
    {
        reserved = null;
        return _marketInventory.TryTakeStock(prototype, amount, out reserved);
    }

    public void ReturnStock(EntityUid station, MarketData stock)
    {
        // Returning an unpaid reservation must also work if its source station disappears.
        if (!_marketInventory.TryAddStock(stock.Prototype, stock.Quantity, stock.Price, stock.StackPrototype))
        {
            Log.Error($"Unable to return {stock.Quantity} units of {stock.Prototype} to the global market after a failed purchase at {station}.");
        }
    }

    /// <summary>
    /// Resolves every intended purchase against current shared stock without reserving anything.
    /// </summary>
    private bool TryGetCartStock(Entity<MarketConsoleComponent> console, out List<MarketData> stock)
    {
        stock = new List<MarketData>(console.Comp.CartDataList.Count);
        var seen = new HashSet<EntProtoId>();
        foreach (var entry in console.Comp.CartDataList)
        {
            if (entry.Quantity <= 0 || !seen.Add(entry.Prototype) ||
                !_marketInventory.TryGetStock(entry.Prototype, out var current) ||
                current.Quantity < entry.Quantity || current.StackPrototype != entry.StackPrototype)
            {
                stock.Clear();
                return false;
            }

            current.Quantity = entry.Quantity;
            stock.Add(current);
        }

        return CalculateEntityAmount(stock) <= 30;
    }

    private void OnMarketInventoryChanged(ref MarketInventoryChangedEvent args)
    {
        Dictionary<float, List<MarketData>>? catalogs = null;
        IReadOnlyList<MarketData>? stock = null;
        MarketSellCeiling? ceiling = null;
        var query = EntityQueryEnumerator<MarketConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!_ui.IsUiOpen(uid, MarketConsoleUiKey.Default))
                continue;

            var balance = _ui.TryGetUiState<MarketConsoleInterfaceState>(uid, MarketConsoleUiKey.Default, out var state)
                ? state.Balance
                : 0;
            var modifier = TryComp<MarketModifierComponent>(uid, out var mod) ? mod.Mod : 1f;
            catalogs ??= new Dictionary<float, List<MarketData>>();
            if (!catalogs.TryGetValue(modifier, out var catalog))
            {
                stock ??= _marketInventory.GetStock();
                ceiling ??= _marketSellCeilings.GetSnapshot();
                catalog = BuildMarketDisplayData(stock, modifier, ceiling);
                catalogs.Add(modifier, catalog);
            }

            // Each catalog is quoted once per modifier in this notification, not once per viewer.
            RefreshState(uid, balance, modifier, console, catalog, ceiling);
        }
    }

    private List<MarketData> BuildMarketDisplayData(IReadOnlyList<MarketData> stock, float modifier,
        MarketSellCeiling? limits = null)
    {
        var display = new List<MarketData>(stock.Count);
        var ceiling = limits ?? _marketSellCeilings.GetSnapshot();
        foreach (var entry in stock)
        {
            var key = _dynamicMarket.GetMarketKeyFromPrototype(entry.Prototype);
            _dynamicMarket.TryGetQuote(key, out var quote);
            MarketPurchaseQuote? purchase = null;
            var available = TryGetMarketDeliveryPrototype(entry, out var deliveryPrototype) &&
                _marketPurchases.TryQuotePrototypeBuy(deliveryPrototype, 1, entry.Price,
                    modifier, out purchase, entry.StackPrototype != null, ceiling: ceiling);
            display.Add(new MarketData(entry.Prototype, entry.StackPrototype, entry.Quantity,
                available ? purchase!.TotalPrice / (double) modifier : 0)
            {
                Available = available,
                Trend = quote.Trend,
                ChangePercent = quote.ChangePercent,
            });
        }

        return display;
    }

    /// <summary>
    /// Includes the actual delivery crate in both the liquidation floor and market pressure.
    /// The configured machine fee remains nominally fixed; it is never omitted from the quote.
    /// </summary>
    private bool TryQuoteMarketCart(
        List<MarketData> dataList,
        float marketModifier,
        EntProtoId cratePrototype,
        int crateFee,
        [NotNullWhen(true)] out MarketPurchaseQuote? quote,
        out int quotedCratePrice,
        List<MarketData>? displayData = null,
        MarketSellCeiling? limits = null)
    {
        quote = null;
        quotedCratePrice = 0;
        var ceiling = limits ?? _marketSellCeilings.GetSnapshot();
        var requests = new List<MarketPurchaseRequest>(dataList.Count + 1)
        {
            new(cratePrototype, 1, 0),
        };
        if (!_marketPurchases.TryQuotePurchase(requests, crateFee, out var crateQuote, ceiling: ceiling))
            return false;

        quotedCratePrice = crateQuote.TotalPrice;
        var previousTotal = quotedCratePrice;
        var ordered = new List<MarketData>(dataList);
        ordered.Sort((a, b) => string.CompareOrdinal(a.Prototype, b.Prototype));
        foreach (var entry in ordered)
        {
            if (!TryGetMarketDeliveryPrototype(entry, out var deliveryPrototype))
                return false;

            requests.Add(new MarketPurchaseRequest(deliveryPrototype, entry.Quantity, entry.Price,
                marketModifier, entry.StackPrototype != null));
            if (displayData == null)
                continue;

            if (!_marketPurchases.TryQuotePurchase(requests, crateFee, out var prefix, ceiling: ceiling))
                return false;

            var lineTotal = prefix.TotalPrice - previousTotal;
            previousTotal = prefix.TotalPrice;
            displayData.Add(new MarketData(entry.Prototype, entry.StackPrototype, entry.Quantity,
                lineTotal / (entry.Quantity * (double)marketModifier)) { LineTotal = lineTotal });
        }

        return _marketPurchases.TryQuotePurchase(requests, crateFee, out quote, ceiling: ceiling);
    }

    private bool TryGetMarketDeliveryPrototype(MarketData entry, out EntProtoId prototype)
    {
        prototype = entry.Prototype;
        if (entry.StackPrototype is not { } stackId)
            return true;

        // SpawnCrateItems uses StackPrototype.Spawn, which can differ from a stored variant's ID.
        if (!_prototypeManager.TryIndex(stackId, out var stack))
            return false;

        prototype = stack.Spawn;
        return true;
    }
}
