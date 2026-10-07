// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>
/// Conservative credit payout bounds captured once for an entire purchase quote or catalog refresh.
/// Gas can be sold through either a gas console or a pallet. Item taxes belong to individual basket lines.
/// Non-cash pallets can also pay those taxes in credits, but their token payouts are not money.
/// FixedCreditPayouts bounds cash exchanges per commodity unit independently of market factors.
/// Trade delivery premiums are earned by hauling and are excluded from the purchase floor.
/// </summary>
public readonly record struct MarketSellCeiling(
    double PalletMultiplier,
    double GasMultiplier,
    double NonCashPalletMultiplier = 0,
    IReadOnlyDictionary<string, double>? FixedCreditPayouts = null);
