using System.Linq;
using Content.Shared._NF.Market;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Market.Extensions;

public static class MarketDataExtensions
{
    /// <summary>
    /// Update-or-insert the market data list or adds it new if it doesnt exist in there yet.
    /// </summary>
    /// <param name="entityPrototypeId">The entity prototype id to change the amount of.</param>
    /// <param name="increaseAmount">The change in units, ie. 6 plushies.</param>
    /// <param name="marketDataList">The market data list to modify.</param>
    /// <param name="estimatedPrice">The estimated price by the pricing system.</param>
    /// <param name="stackPrototypeId">The stack prototype id for this prototype if any.</param>
    // Exodus-begin: validate the whole stock change before mutating quantities or prices.
    public static bool Upsert(this List<MarketData> marketDataList,
        string entityPrototypeId,
        int increaseAmount,
        double estimatedPrice,
        string? stackPrototypeId = null)
    {
        if (increaseAmount == 0)
            return true;

        if (increaseAmount > 0 && (!double.IsFinite(estimatedPrice) || estimatedPrice < 0))
            return false;

        MarketData? prototypeMarketData = null;
        foreach (var entry in marketDataList)
        {
            if (entry.Prototype == entityPrototypeId)
            {
                prototypeMarketData = entry;
                break;
            }
        }

        if (prototypeMarketData == null)
        {
            if (increaseAmount < 0)
                return false;

            marketDataList.Add(new MarketData(entityPrototypeId, stackPrototypeId, increaseAmount, estimatedPrice));
            return true;
        }

        var newQuantity = (long) prototypeMarketData.Quantity + increaseAmount;
        if (newQuantity < 0 || newQuantity > int.MaxValue)
            return false;

        if (newQuantity == 0)
        {
            marketDataList.Remove(prototypeMarketData);
            return true;
        }

        if (increaseAmount > 0)
        {
            var addedShare = increaseAmount / (double) newQuantity;
            prototypeMarketData.Price = double.IsFinite(prototypeMarketData.Price)
                ? prototypeMarketData.Price * (1 - addedShare) + estimatedPrice * addedShare
                : estimatedPrice;
        }

        prototypeMarketData.Quantity = (int) newQuantity;
        return true;
    }
    // Exodus-end

    /// <summary>
    /// Moves a MarketData item from the source list to the target list.
    /// </summary>
    /// <param name="sourceList">The source list to move the item from.</param>
    /// <param name="targetList">The target list to move the item to.</param>
    /// <param name="prototypeId">The prototype ID of the item to move.</param>
    public static void Move(this List<MarketData> sourceList, List<MarketData> targetList, string prototypeId)
    {
        var marketData = sourceList.FirstOrDefault(md => md.Prototype == prototypeId);
        if (marketData != null &&
            targetList.Upsert(marketData.Prototype, marketData.Quantity, marketData.Price, marketData.StackPrototype)) // Exodus: retain source when destination rejects the transfer.
        {
            sourceList.Remove(marketData);
        }
    }

    /// <summary>
    /// Get the current maximum amount available for a particular prototype.
    /// </summary>
    /// <param name="marketDataList">the list to check in</param>
    /// <param name="prototype">the prototype to check for</param>
    /// <returns>The max quantity withdrawable</returns>
    public static int GetMaxQuantityToWithdraw(this List<MarketData> marketDataList, EntityPrototype prototype)
    {
        var marketData = marketDataList.FirstOrDefault(md => md.Prototype == prototype.ID);
        return marketData == null ? 0 : marketData.Quantity;
    }

    /// <summary>
    /// Get the total value of the dataList
    /// </summary>
    /// <param name="dataList">The list to get the value of</param>
    /// <param name="marketModifier">The market modifier to apply</param>
    /// <returns>The total value</returns>
    public static int GetMarketValue(List<MarketData> dataList, float marketModifier)
    {
        // Nothing to buy, no value.
        if (dataList.Count <= 0)
            return 0;

        return dataList.Sum(marketData => (int) Math.Round(marketData.Price * marketData.Quantity * marketModifier));
    }
}
