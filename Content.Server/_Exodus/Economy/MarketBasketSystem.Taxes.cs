// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared._Mono.ItemTax.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private readonly Dictionary<ProtoId<StackPrototype>, double> _stackTaxMultipliers = new();

    /// <summary>
    /// Read current coefficients for the entity's own value. Containers do not tax their contents.
    /// </summary>
    public MarketItemTax GetEntityTax(EntityUid uid)
    {
        return ReadItemTax(CompOrNull<ItemTaxComponent>(uid));
    }

    private MarketItemTax GetPrototypeTax(EntityPrototype prototype)
    {
        return prototype.TryGetComponent<ItemTaxComponent>(out var tax, _factory) ? ReadItemTax(tax) : default;
    }

    private static MarketItemTax ReadItemTax(ItemTaxComponent? tax)
    {
        var result = new MarketItemTax();
        if (tax == null)
            return result;

        foreach (var (account, coefficient) in tax.TaxAccounts)
        {
            if (!float.IsFinite(coefficient))
                continue;

            // These are the accounts actually credited by CargoSystem.OnPalletSale.
            result = account switch
            {
                SectorBankAccount.BlackMarket => result with { BlackMarket = coefficient },
                SectorBankAccount.Frontier => result with { Frontier = coefficient },
                SectorBankAccount.Nfsd => result with { Nfsd = coefficient },
                SectorBankAccount.Medical => result with { Medical = coefficient },
                _ => result,
            };
        }

        return result;
    }

    private void RebuildStackTaxMultipliers()
    {
        _stackTaxMultipliers.Clear();
        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.TryGetComponent<StackComponent>(out var stack, _factory))
                continue;

            var multiplier = GetPrototypeTax(prototype).PositiveMultiplier;
            if (multiplier > _stackTaxMultipliers.GetValueOrDefault(stack.StackTypeId, 1))
                _stackTaxMultipliers[stack.StackTypeId] = multiplier;
        }
    }
}
