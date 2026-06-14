using Content.Server.Store.Conditions; // Exodus
using Content.Shared.GameTicking; // Exodus
using Content.Shared.Store; // Exodus

namespace Content.Server.Store.Systems;

public sealed partial class StoreSystem
{
    // Exodus: Shared limited stock prevents bypassing round quotas by purchasing from multiple stores.
    private readonly Dictionary<string, int> _sharedLimitedStockPurchases = new();

    public int GetSharedLimitedStockPurchases(string stockKey)
    {
        return _sharedLimitedStockPurchases.GetValueOrDefault(stockKey);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _sharedLimitedStockPurchases.Clear();
    }

    // Exodus: Calculates remaining limited stock for UI and availability feedback.
    public int? GetRemainingLimitedStock(ListingData listing)
    {
        if (listing.Conditions == null)
            return null;

        int? remaining = null;
        foreach (var condition in listing.Conditions)
        {
            if (condition is not ListingLimitedStockCondition stockCondition)
                continue;

            var purchased = stockCondition.Shared
                ? GetSharedLimitedStockPurchases(stockCondition.GetStockKey(listing))
                : listing.PurchaseAmount;

            var currentRemaining = Math.Max(0, stockCondition.Stock - purchased);
            remaining = remaining is null
                ? currentRemaining
                : Math.Min(remaining.Value, currentRemaining);
        }

        return remaining;
    }

    // Exodus: Stamps current remaining stock into listing snapshots sent to the client UI.
    public void UpdateLimitedStockState(IEnumerable<ListingDataWithCostModifiers> listings)
    {
        foreach (var listing in listings)
        {
            listing.RemainingStock = GetRemainingLimitedStock(listing);
        }
    }

    // Exodus: Shared helper for custom store flows that complete a purchase outside the default OnBuyRequest path.
    public void MarkListingPurchased(ListingData listing)
    {
        listing.PurchaseAmount++;
        TrackSharedLimitedStock(listing);
        listing.RemainingStock = GetRemainingLimitedStock(listing);
    }

    private void TrackSharedLimitedStock(ListingData listing)
    {
        if (listing.Conditions == null)
            return;

        foreach (var condition in listing.Conditions)
        {
            if (condition is not ListingLimitedStockCondition { Shared: true } stockCondition)
                continue;

            var stockKey = stockCondition.GetStockKey(listing);
            _sharedLimitedStockPurchases[stockKey] = GetSharedLimitedStockPurchases(stockKey) + 1;
        }
    }
}
