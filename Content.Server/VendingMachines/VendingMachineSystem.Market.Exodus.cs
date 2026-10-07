// (c) Space Exodus Team - EXDS-RL with CLA
// Exodus: authoritative purchase quotes and protected paid vending.
using System.Diagnostics.CodeAnalysis;
using Content.Server._Exodus.Economy;
using Content.Server.Advertise.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Economy;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private MarketPurchaseSystem _marketPurchases = default!;
    [Dependency] private DynamicMarketSystem _dynamicMarket = default!;
    [Dependency] private MarketSellCeilingSystem _marketSellCeilings = default!;
    [Dependency] private UserInterfaceSystem _vendingUi = default!;

    private TimeSpan _nextMarketPriceRefresh;

    private void OnMarketUiOpened(Entity<VendingMachineComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(VendingMachineUiKey.Key))
            RefreshMarketPrices(ent);
    }

    private void UpdateMarketPrices()
    {
        if (_timing.CurTime < _nextMarketPriceRefresh)
            return;

        _nextMarketPriceRefresh = _timing.CurTime + TimeSpan.FromSeconds(2);
        MarketSellCeiling? ceiling = null;
        var query = EntityQueryEnumerator<ActiveUserInterfaceComponent, VendingMachineComponent>();
        while (query.MoveNext(out var uid, out _, out var machine))
        {
            if (!_vendingUi.IsUiOpen(uid, VendingMachineUiKey.Key))
                continue;

            ceiling ??= _marketSellCeilings.GetSnapshot();
            RefreshMarketPrices((uid, machine), ceiling);
        }
    }

    private void RefreshMarketPrices(Entity<VendingMachineComponent> ent, MarketSellCeiling? ceiling = null)
    {
        if (!_vendingUi.HasUi(ent.Owner, VendingMachineUiKey.Key))
            return;

        ceiling ??= _marketSellCeilings.GetSnapshot();
        var prices = new Dictionary<EntProtoId, int?>();
        AddMarketPrices(ent, ent.Comp.Inventory, prices, ceiling);
        if (_emag.CheckFlag(ent, EmagType.Interaction))
            AddMarketPrices(ent, ent.Comp.EmaggedInventory, prices, ceiling);
        if (ent.Comp.Contraband)
            AddMarketPrices(ent, ent.Comp.ContrabandInventory, prices, ceiling);

        if (_vendingUi.TryGetUiState<VendingMachinePriceState>(ent.Owner, VendingMachineUiKey.Key, out var previous) &&
            previous.Prices.Count == prices.Count)
        {
            var changed = false;
            foreach (var (prototype, price) in prices)
            {
                if (previous.Prices.TryGetValue(prototype, out var oldPrice) && price == oldPrice)
                    continue;

                changed = true;
                break;
            }

            if (!changed)
                return;
        }

        _vendingUi.SetUiState(ent.Owner, VendingMachineUiKey.Key, new VendingMachinePriceState(prices));
    }

    private void AddMarketPrices(Entity<VendingMachineComponent> ent,
        Dictionary<string, VendingMachineInventoryEntry> inventory, Dictionary<EntProtoId, int?> prices,
        MarketSellCeiling? ceiling)
    {
        foreach (var entry in inventory.Values)
        {
            if (prices.ContainsKey(entry.ID))
                continue;

            prices[entry.ID] = !ent.Comp.RequiresCash ? 0 :
                TryQuoteVend(ent, entry.ID, out var quote, ceiling) ? quote.TotalPrice : null;
        }
    }

    private bool TryQuoteVend(Entity<VendingMachineComponent> ent, EntProtoId prototype,
        [NotNullWhen(true)] out MarketPurchaseQuote? quote, MarketSellCeiling? ceiling = null)
    {
        quote = null;
        if (!_prototypeManager.TryIndex(prototype, out var proto))
            return false;

        var price = _pricing.GetEstimatedPrice(proto);
        if (price == 0)
            price = 20; // Preserve the existing vending nominal fallback.

        if (TryComp<MarketModifierComponent>(ent, out var modifier))
            price *= modifier.Mod;

        var explicitPrice = _pricing.GetEstimatedVendPrice(proto);
        if (explicitPrice > 0)
            price = explicitPrice;

        return _marketPurchases.TryQuotePrototypeBuy(prototype, 1, price, 1, out quote,
            purchaseReturnRate: GetVendingReturnRate(ent, prototype), ceiling: ceiling);
    }

    private double GetVendingReturnRate(Entity<VendingMachineComponent> ent, EntProtoId prototype)
    {
        if (_prototypeManager.TryIndex<VendingMachineInventoryPrototype>(ent.Comp.PackPrototypeId, out var inventory) &&
            inventory.RevenueAccounts.ContainsKey(prototype))
            return 1;

        double rate = 0;
        foreach (var coefficient in ent.Comp.TaxAccounts.Values)
        {
            if (float.IsFinite(coefficient) && coefficient > 0)
                rate += coefficient;
        }

        return rate;
    }

    /// <summary>
    /// Validate before taking payment. BeginMarketVend cannot reject after this reservation check.
    /// </summary>
    private bool TryPrepareMarketVend(Entity<VendingMachineComponent> ent, InventoryType type, string itemId,
        [NotNullWhen(true)] out VendingMachineInventoryEntry? entry)
    {
        entry = null;
        if (Deleted(ent) || ent.Comp.Ejecting || ent.Comp.Broken || !this.IsPowered(ent, EntityManager))
            return false;

        entry = type switch
        {
            InventoryType.Regular => ent.Comp.Inventory.GetValueOrDefault(itemId),
            InventoryType.Contraband when ent.Comp.Contraband => ent.Comp.ContrabandInventory.GetValueOrDefault(itemId),
            InventoryType.Emagged when _emag.CheckFlag(ent, EmagType.Interaction) => ent.Comp.EmaggedInventory.GetValueOrDefault(itemId),
            _ => null,
        };

        if (entry == null || string.IsNullOrEmpty(entry.ID) || !_prototypeManager.HasIndex<EntityPrototype>(entry.ID))
        {
            Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-invalid-item"), ent.Owner);
            Deny(ent.Owner, ent.Comp);
            return false;
        }

        if (entry.Amount == 0)
        {
            Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-out-of-stock"), ent.Owner);
            Deny(ent.Owner, ent.Comp);
            return false;
        }

        return true;
    }

    private void BeginMarketVend(Entity<VendingMachineComponent> ent, VendingMachineInventoryEntry entry, bool throwItem)
    {
        ent.Comp.Ejecting = true;
        ent.Comp.NextItemToEject = entry.ID;
        ent.Comp.ThrowNextItem = throwItem;
        if (entry.Amount != uint.MaxValue)
            entry.Amount--;

        if (TryComp<SpeakOnUIClosedComponent>(ent, out var speak))
            _speakOnUIClosed.TrySetFlag((ent.Owner, speak));

        Dirty(ent);
        TryUpdateVisualState(ent.Owner, ent.Comp);
        Audio.PlayPvs(ent.Comp.SoundVend, ent.Owner);
    }

    private void RejectMarketPurchase(Entity<VendingMachineComponent> ent, EntityUid buyer, string message)
    {
        Popup.PopupEntity(Loc.GetString(message), ent.Owner, buyer);
        Deny(ent.Owner, ent.Comp);
        RefreshMarketPrices(ent);
    }

    private void PurchaseVendorItem(Entity<VendingMachineComponent> ent, EntityUid buyer, InventoryType type,
        string itemId, int? expectedPrice)
    {
        if (Deleted(buyer) || !IsAuthorized(ent.Owner, buyer, ent.Comp) ||
            !TryPrepareMarketVend(ent, type, itemId, out var entry))
            return;

        MarketPurchaseQuote? quote = null;
        if (ent.Comp.RequiresCash && !TryQuoteVend(ent, entry.ID, out quote))
        {
            RejectMarketPurchase(ent, buyer, "market-purchase-unavailable");
            return;
        }

        var totalPrice = quote?.TotalPrice ?? 0;
        if (expectedPrice.HasValue && expectedPrice.Value != totalPrice)
        {
            RejectMarketPurchase(ent, buyer, "market-purchase-price-changed");
            return;
        }

        var bankBalance = 0;
        if (!HasComp<IronmanComponent>(buyer) && TryComp<BankAccountComponent>(buyer, out var bank))
            bankBalance = Math.Max(0, bank.Balance);

        var cashBalance = 0;
        Entity<StackComponent>? cash = null;
        if (ent.Comp.CashSlotName != null && ent.Comp.CurrencyStackType != null &&
            ItemSlots.TryGetSlot(ent, ent.Comp.CashSlotName, out var slot) &&
            slot?.ContainerSlot?.ContainedEntity is { } cashUid &&
            TryComp<StackComponent>(cashUid, out var stack) && stack.StackTypeId == ent.Comp.CurrencyStackType)
        {
            cashBalance = Math.Max(0, stack.Count);
            cash = (cashUid, stack);
        }

        if (totalPrice > (long)bankBalance + cashBalance)
        {
            RejectMarketPurchase(ent, buyer, "bank-insufficient-funds");
            return;
        }

        var cashPayment = Math.Min(cashBalance, totalPrice);
        var bankPayment = totalPrice - cashPayment;
        // Bank authorization is the last operation that can fail. No cash or stock has changed yet.
        if (bankPayment > 0 && !_bankSystem.TryBankWithdraw(buyer, bankPayment))
        {
            RejectMarketPurchase(ent, buyer, "bank-insufficient-funds");
            return;
        }

        if (cashPayment > 0 && cash is { } cashEntity)
        {
            _stack.SetCount(cashEntity.Owner, cashBalance - cashPayment, cashEntity.Comp);
            ent.Comp.CashSlotBalance = cashBalance - cashPayment;
        }

        // Keep the existing zero-price purchase marker for explicitly free grants.
        ent.Comp.LastPurchasePrice = totalPrice;
        BeginMarketVend(ent, entry, ent.Comp.CanShoot);
        if (quote != null)
        {
            _dynamicMarket.CommitTransaction(quote.Transaction);
            CreditVendingRevenue(ent, entry.ID, quote.NominalPrice);
        }

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(buyer):user} bought {entry.ID} from {ToPrettyString(ent)} for {totalPrice} ({cashPayment} cash, {bankPayment} bank).");
        RefreshMarketPrices(ent);
    }

    private void CreditVendingRevenue(Entity<VendingMachineComponent> ent, EntProtoId prototype, int nominalPrice)
    {
        if (_prototypeManager.TryIndex<VendingMachineInventoryPrototype>(ent.Comp.PackPrototypeId, out var inventory) &&
            inventory.RevenueAccounts.TryGetValue(prototype, out var recipient))
        {
            if (nominalPrice > 0 && !_bankSystem.TrySectorDeposit(recipient, nominalPrice, LedgerEntryType.ProductSales))
                Log.Error($"Could not credit {nominalPrice} to {recipient} for vending product {prototype}.");
            return;
        }

        foreach (var (account, coefficient) in ent.Comp.TaxAccounts)
        {
            if (!float.IsFinite(coefficient) || coefficient <= 0)
                continue;

            var tax = DynamicMarketSystem.RoundSellPayout(nominalPrice * (double)coefficient);
            if (tax > 0)
                _bankSystem.TrySectorDeposit(account, tax, LedgerEntryType.VendorTax);
        }
    }
}
