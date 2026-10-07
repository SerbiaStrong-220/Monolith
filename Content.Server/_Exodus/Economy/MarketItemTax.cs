// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>
/// Credit coefficients paid by a pallet for one commodity's own value, independently of its container.
/// Negative transfers are preserved for sales but cannot lower the purchase's credit payout bound.
/// </summary>
public readonly record struct MarketItemTax(
    double BlackMarket = 0,
    double Frontier = 0,
    double Nfsd = 0,
    double Medical = 0)
{
    public double PositiveMultiplier => 1 + Math.Max(0, BlackMarket) + Math.Max(0, Frontier) +
                                        Math.Max(0, Nfsd) + Math.Max(0, Medical);
}
