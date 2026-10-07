using Robust.Shared.Serialization;

namespace Content.Shared._NF.Market.Events;

/// <summary>
///     When the player purchases an item from the market, this message is sent.
/// </summary>
[Serializable, NetSerializable]
public sealed class MarketPurchaseMessage : BoundUserInterfaceMessage
{
    // Exodus-begin: refuse checkout when the displayed total is stale or absent.
    public int? ExpectedPrice;

    public MarketPurchaseMessage(int? expectedPrice = null)
    {
        ExpectedPrice = expectedPrice;
    }
    // Exodus-end
}

