using System.Linq;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Extensions;
using Content.Shared._NF.Market;
using Content.Shared._NF.Market.BUI;
using Content.Shared._NF.Market.Events;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Server._Exodus.Economy; // Exodus dynamic market
using Content.Shared._NF.CrateMachine.Components; // Exodus delivery crate appraisal
using Robust.Shared.Prototypes;


namespace Content.Server._NF.Market.Systems;

public sealed partial class MarketSystem
{

    private void InitializeConsole()
    {
        // Exodus: completed sales are received centrally by MarketStockIntakeSystem.
        SubscribeLocalEvent<MarketConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<MarketConsoleComponent, MarketConsoleCartMessage>(OnCartMessage);
        SubscribeLocalEvent<MarketConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<MarketInventoryChangedEvent>(OnMarketInventoryChanged); // Exodus: refresh open terminals after shared stock changes.
    }

    private void OnPowerChanged(EntityUid uid, MarketConsoleComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;
        _ui.CloseUi(uid, MarketConsoleUiKey.Default);
    }

    // Exodus: stock intake and nested-container accounting live in MarketStockIntakeSystem.

    /// <summary>
    /// Calculates the total number of entities in the market data list, taking into account the maximum stack count for stackable items.
    /// </summary>
    /// <param name="marketDataList">The list of market data to calculate the total entity count from.</param>
    /// <returns>The total number of entities in the market data list.</returns>
    public int CalculateEntityAmount(IReadOnlyList<MarketData> marketDataList) // Exodus: accept shared inventory snapshots.
    {
        // Exodus-begin: count every non-stack item, with bounded arithmetic for untrusted quantities.
        long count = 0;
        foreach (var data in marketDataList)
        {
            if (data.Quantity <= 0)
                continue;

            if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                if (stackPrototype.MaxCount is { } maxCount)
                {
                    var capacity = Math.Max(1, maxCount);
                    count += ((long)data.Quantity + capacity - 1) / capacity;
                }
                else
                    count++;
            }
            else
                count += data.Quantity;

            if (count >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)count;
        // Exodus-end
    }

    /// <summary>
    /// Calculates the amount of items that can fit within an entity's worth of space for a given item type.
    /// </summary>
    /// <param name="data">The item type to calculate.</param>
    /// <returns>The number of items that can fit within an entity's worth of space. Null if infinite.</returns>
    public int? GetAmountPerEntitySpace(MarketData data)
    {
        if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
        {
            var maxStackCount = stackPrototype.MaxCount;
            if (maxStackCount != null)
                return int.Max(1, maxStackCount.Value); // Ensure amount is positive.
            else
                return null; // Infinity.
        }
        else
        {
            return 1;
        }
    }

    /// <summary>
    /// Occurs whenever something is added to the cart.
    /// If args.Amount is too high it will automatically be clamped to the maximum amount possible.
    /// This also prevents desync if there are two different consoles.
    /// </summary>
    /// <param name="consoleUid">The uuid of the console where it was added.</param>
    /// <param name="consoleComponent">The console component</param>
    /// <param name="args">The arguments for the cart event</param>
    private void OnCartMessage(
        EntityUid consoleUid,
        MarketConsoleComponent consoleComponent,
        ref MarketConsoleCartMessage args
    )
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (!TryComp<BankAccountComponent>(player, out var bank))
            return;
        var marketMultiplier = 1.0f;
        if (TryComp<MarketModifierComponent>(consoleUid, out var priceMod))
        {
            marketMultiplier = priceMod.Mod;
        }

        // Try to get the EntityPrototype that matches marketData.Prototype
        if (!_prototypeManager.TryIndex<EntityPrototype>(args.ItemPrototype!, out var prototype))
        {
            return; // Skip this iteration if the prototype was not found
        }

        // Exodus-begin: a cart is an intention, so abandoning it cannot reserve global stock.
        var existingCart = FindMarketDataByPrototype(consoleComponent.CartDataList, args.ItemPrototype!);
        if (args.RemoveFromCart)
        {
            if (existingCart != null)
                consoleComponent.CartDataList.Remove(existingCart);
        }
        else
        {
            if (args.Amount <= 0 || !_marketInventory.TryGetStock(prototype.ID, out var existing))
                return;

            var maxQuantityToWithdraw = existing.Quantity - (existingCart?.Quantity ?? 0);
            if (maxQuantityToWithdraw <= 0)
                return;

            var toWithdraw = Math.Min(args.Amount, maxQuantityToWithdraw);

            // Calculate maximum we can fit.
            var entityAmount = CalculateEntityAmount(consoleComponent.CartDataList);
            var amountPerEntity = GetAmountPerEntitySpace(existing);
            long amountLeft;
            if (amountPerEntity == null)
            {
                amountLeft = int.MaxValue; // Infinite stack, infinite space.
            }
            else
            {
                amountLeft = (30L - entityAmount) * amountPerEntity.Value;

                if (existingCart != null)
                {
                    // Find if there's a partially filled entity in the cart.
                    var quantityMod = existingCart.Quantity % amountPerEntity.Value;
                    if (quantityMod != 0)
                    {
                        amountLeft += amountPerEntity.Value - quantityMod;
                    }
                }
                amountLeft = Math.Max(0, amountLeft);
            }

            toWithdraw = (int) Math.Min(toWithdraw, amountLeft);

            if (toWithdraw > 0)
                consoleComponent.CartDataList.Upsert(existing.Prototype, toWithdraw, existing.Price, existing.StackPrototype);
        }
        // Exodus-end

        RefreshState(
            consoleUid,
            bank.Balance,
            marketMultiplier,
            consoleComponent
        );
    }

    /// <summary>
    /// Finds a MarketData item in the list that has the same prototype.
    /// </summary>
    /// <param name="marketDataList">The list of market data to search in.</param>
    /// <param name="prototypeId">The prototype ID to search for.</param>
    /// <returns>The MarketData item with the matching prototype, or null if not found.</returns>
    public MarketData? FindMarketDataByPrototype(List<MarketData> marketDataList, string prototypeId)
    {
        foreach (var marketData in marketDataList)
        {
            if (marketData.Prototype == prototypeId)
            {
                return marketData;
            }
        }
        return null;
    }

    private void OnConsoleUiOpened(EntityUid uid, MarketConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true } player)
            return;
        if (!TryComp<BankAccountComponent>(player, out var bank))
            return;
        var marketMultiplier = 1.0f;
        if (TryComp<MarketModifierComponent>(uid, out var priceMod))
        {
            marketMultiplier = priceMod.Mod;
        }

        RefreshState(uid,
            bank.Balance,
            marketMultiplier,
            component);
    }

    private void RefreshState(
        EntityUid consoleUid,
        int balance,
        float marketMultiplier,
        MarketConsoleComponent? component,
        List<MarketData>? displayMarket = null, // Exodus: reuse the shared catalog during an inventory notification.
        MarketSellCeiling? ceiling = null // Exodus
    )
    {
        if (!Resolve(consoleUid, ref component))
            return;

        // Ensures that when this console is no longer attached to a grid and is powered somehow, it won't work.
        if (Transform(consoleUid).GridUid == null)
            return;

        // Exodus-begin: every terminal sees the same server-wide stock.
        var cartData = component.CartDataList;
        if (displayMarket == null)
        {
            var marketData = _marketInventory.GetStock();
            ceiling ??= _marketSellCeilings.GetSnapshot();
            displayMarket = BuildMarketDisplayData(marketData, marketMultiplier, ceiling);
        }
        // Exodus-end

        // Exodus: cart balance uses sequential sector factor + local console MarketModifier (marketMultiplier).
        var displayCart = new List<MarketData>(cartData.Count);
        var cartBalance = 0;
        var cratePrice = component.TransactionCost;
        var canPurchase = false;
        if (cartData.Count > 0 && TryGetCartStock((consoleUid, component), out var liveCart) && // Exodus: quote only nonempty, available carts.
            _crateMachine.FindNearestUnoccupied(consoleUid, component.MaxCrateMachineDistance, out var machine) &&
            TryComp<CrateMachineComponent>(machine, out var crateMachine) &&
            TryQuoteMarketCart(liveCart, marketMultiplier, crateMachine.CratePrototype, component.TransactionCost,
                out var cartQuote, out cratePrice, displayCart, ceiling))
        {
            cartBalance = cartQuote.TotalPrice - cratePrice;
            canPurchase = cartData.Count > 0;
        }
        else
        {
            displayCart.Clear();
            // Exodus: do not expose mutable cart entries or show a stale payable line total.
            foreach (var entry in cartData)
                displayCart.Add(new MarketData(entry.Prototype, entry.StackPrototype, entry.Quantity, 0) { Available = false });
        }

        var newState = new MarketConsoleInterfaceState(
            balance,
            marketMultiplier,
            displayMarket, // Exodus: sector-adjusted display
            displayCart, // Exodus: sequential execution prices matching CartBalance
            cartBalance,
            true, // TODO add enable/disable functionality
            cratePrice,
            CalculateEntityAmount(cartData),
            canPurchase // Exodus: unsafe, stale or unavailable delivery cannot be purchased.
        );
        _ui.SetUiState(consoleUid, MarketConsoleUiKey.Default, newState);
    }
}
