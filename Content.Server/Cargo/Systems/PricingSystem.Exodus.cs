// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

// Exodus: default appraisal integration for items without a base price.
public sealed partial class PricingSystem
{
    [Dependency] private DefaultItemPriceSystem _defaultItemPrice = default!;

    private double ApplyUnpricedFallback(EntityUid uid, double price)
    {
        return _defaultItemPrice.ApplyFallback(uid, price);
    }

    private double ApplyUnpricedFallback(EntityPrototype prototype, double price)
    {
        return _defaultItemPrice.ApplyFallback(prototype, price);
    }

    // Exodus: splitting changes the stack count but preserves fixed static and solution appraisals.
    public double GetEstimatedSingleStackPrice(EntityPrototype prototype, out bool handled)
    {
        var price = GetEstimatedPrice(prototype, out handled, applyFallback: false);
        if (handled || !prototype.TryGetComponent<StackComponent>(out var stack, Factory) || stack.Count <= 0)
            return price;

        return price - (GetMaterialsPrice(prototype) + GetStackPrice(prototype)) * (1 - 1.0 / stack.Count);
    }
}
