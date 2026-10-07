// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared._NF.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    /// <summary>
    /// Maximum resale reward without completing a delivery. Destination, express and wildcard
    /// rewards pay for hauling and must not raise the purchase floor. Live sales still use the
    /// runtime price event, including the actual destination and deadline.
    /// </summary>
    private double GetTradeCrateElsewherePriceBound(EntityPrototype prototype)
    {
        if (!prototype.TryGetComponent<TradeCrateComponent>(out var crate, _factory))
            return 0;

        // A negative late penalty can increase resale value merely by waiting at the source.
        // Convert before subtraction so two valid int fields cannot overflow the calculation.
        var elsewhere = (double) crate.ValueElsewhere;
        var best = Math.Max(0, elsewhere);
        if (crate.ExpressDeliveryDuration > TimeSpan.Zero)
            best = Math.Max(best, elsewhere - crate.ExpressLatePenalty);

        return best;
    }
}
