// (c) Space Exodus Team - EXDS-RL with CLA
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Quotes all paid NPC purchases against a common resale bound, excluding earned delivery rewards.
/// Prototype composition is cached separately; no entity spawning or world scan occurs per item.
/// </summary>
public sealed partial class MarketPurchaseSystem : EntitySystem
{
    [Dependency] private MarketBasketSystem _baskets = default!;
    [Dependency] private MarketSellCeilingSystem _ceilings = default!;
    [Dependency] private DynamicMarketSystem _market = default!;
    [Dependency] private MarketSettingsSystem _settings = default!;

    private struct Commodity
    {
        public double Units;
        public double CommittedUnits;
        public double NominalValue;
        public double MaximumResaleUnitPrice;
        public double MaximumFixedResaleUnitPrice;
    }

    public bool TryQuotePrototypeBuy(
        EntProtoId prototype,
        int quantity,
        double unitBasePrice,
        double modifier,
        [NotNullWhen(true)] out MarketPurchaseQuote? quote,
        bool stackUnits = false,
        double purchaseReturnRate = 0,
        MarketSellCeiling? ceiling = null)
    {
        return TryQuotePurchase(
            [new MarketPurchaseRequest(prototype, quantity, unitBasePrice, modifier, stackUnits)],
            0, out quote, purchaseReturnRate, ceiling);
    }

    /// <summary>
    /// Returns false for unknown composition, invalid amounts and unrepresentable totals.
    /// purchaseReturnRate bounds nominal purchase revenue returned to any potentially cooperating accounts.
    /// additionalCost is a non-refundable service charge, not the price of an extra spawned container.
    /// </summary>
    public bool TryQuotePurchase(
        IReadOnlyList<MarketPurchaseRequest> requests,
        double additionalCost,
        [NotNullWhen(true)] out MarketPurchaseQuote? quote,
        double purchaseReturnRate = 0,
        MarketSellCeiling? ceiling = null)
    {
        quote = null;
        if (!_market.Ready || requests.Count == 0 || !double.IsFinite(additionalCost) || additionalCost < 0 ||
            !double.IsFinite(purchaseReturnRate) || purchaseReturnRate < 0)
            return false;

        var limits = ceiling ?? _ceilings.GetSnapshot();
        if (!double.IsFinite(limits.PalletMultiplier) || limits.PalletMultiplier < 1 ||
            !double.IsFinite(limits.GasMultiplier) || limits.GasMultiplier < limits.PalletMultiplier ||
            !double.IsFinite(limits.NonCashPalletMultiplier) || limits.NonCashPalletMultiplier < 0)
            return false;

        var commodities = new Dictionary<string, Commodity>(StringComparer.Ordinal);
        List<(MarketBasket Basket, double Scale)>? fixedWrappers = null;
        HashSet<string>? wrappedCommodities = null;
        foreach (var request in requests)
        {
            if (request.Quantity <= 0 || !double.IsFinite(request.UnitBasePrice) || request.UnitBasePrice < 0 ||
                !double.IsFinite(request.Modifier) || request.Modifier <= 0 ||
                !_baskets.TryGetPrototypeBasket(request.Prototype, out var basket, out _) ||
                basket.SpawnedUnits <= 0 || !double.IsFinite(basket.NominalValue) || basket.NominalValue < 0)
                return false;

            var scale = request.StackUnits ? (double)request.Quantity / basket.SpawnedUnits : request.Quantity;
            var nominal = request.UnitBasePrice * request.Quantity * request.Modifier;
            if (!double.IsFinite(nominal))
                return false;

            var hasFixedPackage = false;
            for (var index = 0; index < basket.Packages.Count; index++)
            {
                var package = basket.Packages[index];
                var payout = limits.FixedCreditPayouts?.GetValueOrDefault(DynamicMarketSystem.ProtoKey(package.Prototype.Id)) ?? 0;
                if (!double.IsFinite(payout) || payout < 0 || !double.IsFinite(package.Quantity) || package.Quantity <= 0 ||
                    package.Uses <= 0 || package.FirstLine < 0 || package.LineCount <= 0 ||
                    package.FirstLine > basket.Lines.Count - package.LineCount ||
                    package.FirstNestedPackage < 0 || package.FirstNestedPackage > index)
                {
                    return false;
                }
                if (payout == 0)
                    continue;

                hasFixedPackage = true;
                for (var lineIndex = package.FirstLine; lineIndex < package.FirstLine + package.LineCount; lineIndex++)
                    (wrappedCommodities ??= new(StringComparer.Ordinal)).Add(basket.Lines[lineIndex].MarketKey);
            }
            if (hasFixedPackage)
                (fixedWrappers ??= new()).Add((basket, scale));

            var allocated = false;
            foreach (var line in basket.Lines)
            {
                var units = line.Quantity * scale;
                var taxMultiplier = Math.Max(line.Tax.PositiveMultiplier, line.ResaleTaxMultiplier ?? 1);
                if (!double.IsFinite(units) || units <= 0 || !double.IsFinite(line.UnitBasePrice) || line.UnitBasePrice < 0 ||
                    !double.IsFinite(taxMultiplier) || taxMultiplier < 1 ||
                    line.ResaleUnitPrice is { } resaleBound && (!double.IsFinite(resaleBound) || resaleBound < 0))
                    return false;

                var value = line.UnitBasePrice * line.Quantity;
                // A zero-valued product may still have an explicit catalog charge and market pressure.
                var nominalShare = basket.NominalValue > 0 ? nominal * (value / basket.NominalValue) : allocated ? 0 : nominal;
                allocated = true;
                // The purchased contents may be unpacked or transferred into another container.
                // A wrapper's IgnoreMarketModifier must not lower their global liquidation bound.
                var multiplier = line.MarketKey.StartsWith("gas:", StringComparison.Ordinal)
                    ? limits.GasMultiplier
                    : limits.PalletMultiplier;
                // Token sellers may still credit monetary ItemTax to cooperating sector accounts.
                // Count that credit payout separately without treating their tokens as cash.
                var creditMultiplier = Math.Max(multiplier * taxMultiplier,
                    limits.NonCashPalletMultiplier * (taxMultiplier - 1));
                var resaleUnit = Math.Max(line.UnitBasePrice, line.ResaleUnitPrice ?? 0) * creditMultiplier;
                var fixedPayout = limits.FixedCreditPayouts?.GetValueOrDefault(line.MarketKey) ?? 0;
                if (!double.IsFinite(fixedPayout) || fixedPayout < 0)
                    return false;

                commodities.TryGetValue(line.MarketKey, out var commodity);
                commodity.Units += units;
                // Random contents are a finite upper bound, not a delivery that has actually occurred.
                // Quote every possible branch but never create demand for hypothetical inventory.
                if (basket.Exact)
                    commodity.CommittedUnits += units;
                commodity.NominalValue += nominalShare;
                // One upper unit value for a commodity also bounds splitting, reordering and mixing
                // differently appraised variants of the same stack. It deliberately overestimates.
                commodity.MaximumResaleUnitPrice = Math.Max(commodity.MaximumResaleUnitPrice, resaleUnit);
                commodity.MaximumFixedResaleUnitPrice = Math.Max(commodity.MaximumFixedResaleUnitPrice, fixedPayout);
                if (!double.IsFinite(commodity.Units) || !double.IsFinite(commodity.NominalValue) ||
                    !double.IsFinite(commodity.MaximumResaleUnitPrice))
                    return false;

                commodities[line.MarketKey] = commodity;
            }

            if (!allocated)
                return false;
        }

        var transaction = new MarketTransactionState();
        double normalPrice = additionalCost;
        foreach (var (key, commodity) in commodities)
        {
            // Unit price one ensures even free nominal goods contribute their actual commodity volume.
            var integral = _market.CalculateSequentialBuyCost(key, 1, commodity.Units, 1, 1, transaction, false);
            normalPrice += integral * commodity.NominalValue / commodity.Units;
        }

        var reverseTransaction = new MarketTransactionState();
        foreach (var (key, factor) in transaction.Factors)
            reverseTransaction.Set(key, factor);

        double resale = 0;
        Dictionary<string, double>? resaleUnitCeilings = fixedWrappers == null ? null : new(StringComparer.Ordinal);
        foreach (var (key, commodity) in commodities)
        {
            if (commodity.MaximumFixedResaleUnitPrice > 0 || wrappedCommodities?.Contains(key) == true)
            {
                // A buyer can mix fixed exchanges with market sales without reducing the factor
                // for exchanged units. The post-purchase marginal price bounds every such ordering.
                var factor = transaction.Factors.GetValueOrDefault(key, _market.GetFactor(key));
                var unitCeiling = Math.Max(commodity.MaximumFixedResaleUnitPrice,
                    commodity.MaximumResaleUnitPrice * factor);
                if (resaleUnitCeilings != null)
                    resaleUnitCeilings[key] = unitCeiling;
                resale += commodity.Units * unitCeiling;
            }
            else
            {
                resale += _market.CalculateSequentialSellValue(key, commodity.MaximumResaleUnitPrice,
                    commodity.Units, 1, 1, reverseTransaction, false);
            }
        }

        if (fixedWrappers != null && resaleUnitCeilings != null)
        {
            foreach (var wrapper in fixedWrappers)
            {
                var packages = wrapper.Basket.Packages;
                var adjustments = new double[packages.Count + 1];
                for (var index = 0; index < packages.Count; index++)
                {
                    var package = packages[index];
                    var payout = limits.FixedCreditPayouts?.GetValueOrDefault(DynamicMarketSystem.ProtoKey(package.Prototype.Id)) ?? 0;
                    adjustments[index + 1] = adjustments[index];
                    if (payout == 0)
                        continue;

                    // Nested package adjustments are a contiguous prefix range, keeping traversal
                    // bounded even when many wrappers contain the same commodity or other wrappers.
                    var contents = adjustments[index] - adjustments[package.FirstNestedPackage];
                    for (var lineIndex = package.FirstLine; lineIndex < package.FirstLine + package.LineCount; lineIndex++)
                    {
                        var line = wrapper.Basket.Lines[lineIndex];
                        contents += line.Quantity * wrapper.Scale * resaleUnitCeilings[line.MarketKey];
                    }

                    // Multi-use packages can release earlier payloads and still be redeemed before
                    // their last use. Single-use wrappers choose strictly between whole and unpacked.
                    var whole = payout * package.Quantity * wrapper.Scale + contents * (package.Uses - 1) / package.Uses;
                    adjustments[index + 1] += Math.Max(0, whole - contents);
                }
                resale += adjustments[packages.Count];
            }
        }

        if (!TryRoundCharge(normalPrice, out var nominalPrice) || !double.IsFinite(resale))
            return false;

        // Revenue shares are based only on the nominal integer charge. Adding them to the lower
        // bound prevents the buyer recovering the adjustment through their own faction/company.
        var protectedPrice = resale * (1 + _settings.Current.PurchaseMargin) + nominalPrice * purchaseReturnRate;
        if (!TryRoundCharge(Math.Max(normalPrice, protectedPrice), out var totalPrice))
            return false;

        var committedTransaction = new MarketTransactionState();
        foreach (var (key, commodity) in commodities)
        {
            if (commodity.CommittedUnits > 0)
                _market.CalculateSequentialBuyCost(key, 1, commodity.CommittedUnits, 1, 1, committedTransaction, false);
        }

        quote = new MarketPurchaseQuote(nominalPrice, Math.Max(1, totalPrice), committedTransaction);
        return true;
    }

    private static bool TryRoundCharge(double value, out int rounded)
    {
        rounded = 0;
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue)
            return false;

        rounded = (int)Math.Ceiling(value);
        return true;
    }
}
