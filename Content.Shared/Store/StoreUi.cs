using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Store;

[Serializable, NetSerializable]
public enum StoreUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class StoreUpdateState : BoundUserInterfaceState
{
    public readonly HashSet<ListingDataWithCostModifiers> Listings;

    public readonly Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> Balance;

    public readonly bool ShowFooter;

    public readonly bool AllowRefund;

    // Exodus
    public readonly StoreUiMode Mode;

    // Exodus
    public readonly bool HasPriceModifier;

    // Exodus
    public readonly float PriceMultiplier;

    // Exodus
    public readonly float SummoningPriceMultiplier;

    // Exodus
    public readonly StoreSummoningUiData? ActiveSummoning;

    public StoreUpdateState(
        HashSet<ListingDataWithCostModifiers> listings,
        Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> balance,
        bool showFooter,
        bool allowRefund,
        StoreUiMode mode = StoreUiMode.Default,
        bool hasPriceModifier = false,
        float priceMultiplier = 0f,
        float summoningPriceMultiplier = 1f,
        StoreSummoningUiData? activeSummoning = null)
    {
        Listings = listings;
        Balance = balance;
        ShowFooter = showFooter;
        AllowRefund = allowRefund;
        Mode = mode;
        HasPriceModifier = hasPriceModifier;
        PriceMultiplier = priceMultiplier;
        SummoningPriceMultiplier = summoningPriceMultiplier;
        ActiveSummoning = activeSummoning;
    }
}

// Exodus
[Serializable, NetSerializable]
public enum StoreUiMode : byte
{
    Default,
    Summoning
}

// Exodus
[Serializable, NetSerializable]
public sealed class StoreSummoningUiData(
    ProtoId<ListingPrototype> listingId,
    TimeSpan duration,
    TimeSpan remaining,
    bool paused)
{
    public ProtoId<ListingPrototype> ListingId = listingId;
    public TimeSpan Duration = duration;
    public TimeSpan Remaining = remaining;
    public bool Paused = paused;
}

[Serializable, NetSerializable]
public sealed class StoreRequestUpdateInterfaceMessage : BoundUserInterfaceMessage
{

}

[Serializable, NetSerializable]
public sealed class StoreBuyListingMessage(ProtoId<ListingPrototype> listing) : BoundUserInterfaceMessage
{
    public ProtoId<ListingPrototype> Listing = listing;
}

[Serializable, NetSerializable]
public sealed class StoreRequestWithdrawMessage : BoundUserInterfaceMessage
{
    public string Currency;

    public int Amount;

    public StoreRequestWithdrawMessage(string currency, int amount)
    {
        Currency = currency;
        Amount = amount;
    }
}

/// <summary>
///     Used when the refund button is pressed
/// </summary>
[Serializable, NetSerializable]
public sealed class StoreRequestRefundMessage : BoundUserInterfaceMessage
{

}
