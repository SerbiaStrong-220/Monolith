using Content.Server.Store.Systems; // Exodus
using Content.Shared.Store;

namespace Content.Server.Store.Conditions;

/// <summary>
/// Only allows a listing to be purchased a certain amount of times.
/// </summary>
public sealed partial class ListingLimitedStockCondition : ListingCondition
{
    /// <summary>
    /// The amount of times this listing can be purchased.
    /// </summary>
    [DataField("stock", required: true)]
    public int Stock;

    // Exodus: When enabled, the stock is shared across all stores instead of using the local store cache.
    [DataField("shared")]
    public bool Shared;

    // Exodus: Optional shared stock pool id. Defaults to the listing id.
    [DataField("stockKey")]
    public string? StockKey;

    public override bool Condition(ListingConditionArgs args)
    {
        if (Shared)
        {
            var storeSystem = args.EntityManager.System<StoreSystem>();
            return storeSystem.GetSharedLimitedStockPurchases(GetStockKey(args.Listing)) < Stock;
        }

        return args.Listing.PurchaseAmount < Stock;
    }

    public string GetStockKey(ListingData listing)
    {
        return string.IsNullOrWhiteSpace(StockKey)
            ? listing.ID
            : StockKey;
    }
}
