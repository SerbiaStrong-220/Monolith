// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Cargo.Components;
using Content.Server.Materials.Components;
using Content.Shared.Armor;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private bool TryGetPrototypeOwnPrice(EntityPrototype prototype, out double price, BuildState state,
        bool singleStack = false)
    {
        price = singleStack
            ? _pricing.GetEstimatedSingleStackPrice(prototype, out var handled, includeSolutions: false)
            : _pricing.GetEstimatedPrice(prototype, out handled, applyFallback: false, includeSolutions: false);
        if (handled)
        {
            // An estimated full-price override does not describe its delivered contents.
            return state.Fail($"Opaque estimated appraisal for {prototype.ID} requires a basket adapter.");
        }

        double eventPrice = 0;
        if (prototype.TryGetComponent<ArmorComponent>(out var armor, _factory))
        {
            foreach (var (type, coefficient) in armor.Modifiers.Coefficients)
            {
                if (!_prototypes.TryIndex<DamageTypePrototype>(type, out var damage))
                    return state.Fail($"Unknown armor damage type {type} in {prototype.ID}.");

                eventPrice += armor.PriceMultiplier * damage.ArmorPriceCoefficient * 100 * (1 - coefficient);
            }
            foreach (var (type, reduction) in armor.Modifiers.FlatReduction)
            {
                if (!_prototypes.TryIndex<DamageTypePrototype>(type, out var damage))
                    return state.Fail($"Unknown armor damage type {type} in {prototype.ID}.");

                eventPrice += armor.PriceMultiplier * damage.ArmorPriceFlat * reduction;
            }
        }

        if (prototype.TryGetComponent<BatteryComponent>(out var battery, _factory))
            eventPrice += battery.CurrentCharge * battery.PricePerJoule;

        if (prototype.TryGetComponent<MobPriceComponent>(out var mob, _factory) &&
            prototype.TryGetComponent<MobStateComponent>(out var mobState, _factory) &&
            prototype.TryGetComponent<BodyComponent>(out _, _factory))
        {
            eventPrice += mob.Price * (mobState.CurrentState == MobState.Alive ? 1 : mob.DeathPenalty) *
                          (prototype.TryGetComponent<LabGrownComponent>(out _, _factory) ? 1 : mob.LabGrownPenalty);
        }

        // The current restock handler clears event contributions, then PricingSystem adds static/material values.
        if (prototype.Components.ContainsKey("VendingMachineRestock"))
            eventPrice = 0;

        price += eventPrice + GetTradeCrateElsewherePriceBound(prototype);

        var appraisal = new MarketBasketPriceEvent(prototype, price);
        foreach (var component in prototype.Components.Keys)
        {
            // These runtime handlers require state not available in an entity prototype. New such
            // systems must supply a MarketBasketPriceEvent adapter instead of relying on estimated price.
            if (component is "XenoArtifact" or "DriftingPrice" or "Ghost" or
                "CargoBountyLabel" or "AnomalyCore" or "ScuttleDevice" or "DeepFried")
            {
                appraisal.Failure = $"Stateful appraisal {component} on {prototype.ID} requires a basket adapter.";
                break;
            }
        }

        RaiseLocalEvent(ref appraisal);
        if (appraisal.Failure != null)
            return state.Fail(appraisal.Failure);

        state.Exact &= appraisal.Exact;
        // Applying fallback after the runtime adapters avoids adding it on top of armor or battery value.
        price = _fallback.ApplyFallback(prototype, appraisal.OwnPrice);
        if (singleStack && price > appraisal.OwnPrice &&
            prototype.TryGetComponent<Content.Shared.Stacks.StackComponent>(out var stack, _factory) && stack.Count > 0)
        {
            price = Math.Max(appraisal.OwnPrice, price / stack.Count);
        }
        return double.IsFinite(price) && price >= 0 || state.Fail($"Invalid own appraisal for {prototype.ID}.");
    }
}
