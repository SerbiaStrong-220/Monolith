// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>
/// Quotes the market resale value of an entity and its contents on demand.
/// </summary>
public sealed partial class MarketAppraisalSystem : EntitySystem
{
    [Dependency] private MarketBasketSystem _baskets = default!;
    [Dependency] private DynamicMarketSystem _market = default!;

    /// <summary>
    /// Estimates a sale at a neutral terminal without changing market quotes.
    /// The optional grid applies the same vending resale discounts as a pallet on that grid.
    /// </summary>
    public bool TryGetEntitySellPrice(EntityUid uid, out double price, EntityUid? appraisalGrid = null)
    {
        price = 0;
        if (!_market.Ready || Deleted(uid) || !Initialized(uid) ||
            !_baskets.TryGetEntityBasket(uid, out var basket, out _, appraisalGrid))
        {
            return false;
        }

        // Single items avoid allocating and sorting another collection.
        if (basket.Lines.Count == 1)
        {
            var line = basket.Lines[0];
            price = _market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                1, 1, null, applyImpact: false);
            return true;
        }

        if (basket.Lines.Count == 0)
            return true;

        // Match pallet sale order: cheaper units of a shared commodity consume pressure first.
        var lines = new List<MarketBasketLine>(basket.Lines);
        lines.Sort(static (a, b) =>
        {
            var comparison = string.CompareOrdinal(a.MarketKey, b.MarketKey);
            return comparison != 0 ? comparison : a.UnitBasePrice.CompareTo(b.UnitBasePrice);
        });

        var transaction = new MarketTransactionState();
        foreach (var line in lines)
        {
            price += _market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                1, 1, transaction, applyImpact: false);
        }

        return true;
    }
}
