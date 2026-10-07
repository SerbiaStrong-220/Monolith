using Content.Server._NF.CrateMachine;
using Content.Server._Exodus.Economy; // Exodus bounded dynamic market prices
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Extensions;
using Content.Shared._NF.Market;
using Content.Shared._NF.Market.Components;
using Content.Shared._NF.Market.Events;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Content.Shared._NF.CrateMachine.Components;

namespace Content.Server._NF.Market.Systems;

public sealed partial class MarketSystem
{
    [Dependency] private CrateMachineSystem _crateMachine = default!;

    private void InitializeCrateMachine()
    {
        SubscribeLocalEvent<MarketConsoleComponent, MarketPurchaseMessage>(OnMarketConsolePurchaseCrateMessage);
        SubscribeLocalEvent<CrateMachineComponent, CrateMachineOpenedEvent>(OnCrateMachineOpened);
    }

    private void OnMarketConsolePurchaseCrateMessage(EntityUid consoleUid,
        MarketConsoleComponent component,
        ref MarketPurchaseMessage args)
    {
        var marketMod = 1f;
        if (TryComp<MarketModifierComponent>(consoleUid, out var marketModComponent))
        {
            marketMod = marketModComponent.Mod;
        }

        if (!_crateMachine.FindNearestUnoccupied(consoleUid, component.MaxCrateMachineDistance, out var machineUid) || !_entityManager.TryGetComponent<CrateMachineComponent> (machineUid, out var comp))
        {
            _popup.PopupEntity(Loc.GetString("market-no-crate-machine-available"), consoleUid, Filter.PvsExcept(consoleUid), true);
            _audio.PlayPredicted(component.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

            return;
        }
        OnPurchaseCrateMessage(machineUid.Value, consoleUid, comp, component, marketMod, args);
    }

    private void OnPurchaseCrateMessage(EntityUid crateMachineUid,
        EntityUid consoleUid,
        CrateMachineComponent component,
        MarketConsoleComponent consoleComponent,
        float marketMod,
        MarketPurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryComp<BankAccountComponent>(player, out var bankAccount))
            return;

        TrySpawnCrate(crateMachineUid, player, consoleUid, component, consoleComponent, marketMod, bankAccount, args.ExpectedPrice); // Exodus: bind payment to the displayed quote.
    }

    private void TrySpawnCrate(EntityUid crateMachineUid,
        EntityUid player,
        EntityUid consoleUid,
        CrateMachineComponent component,
        MarketConsoleComponent consoleComponent,
        float marketMod,
        BankAccountComponent playerBank,
        int? expectedPrice) // Exodus
    {
        if (consoleComponent.CartDataList.Count == 0 || // Exodus: reject stale purchases after a successful checkout.
            !TryComp<MarketItemSpawnerComponent>(crateMachineUid, out var itemSpawner))
            return;

        // Exodus-begin: a cart contains intentions; another station may have bought these units.
        if (!TryGetCartStock((consoleUid, consoleComponent), out var liveCart))
        {
            _popup.PopupEntity(Loc.GetString("market-purchase-stock-changed"), consoleUid, player);
            RefreshState(consoleUid, playerBank.Balance, marketMod, consoleComponent);
            return;
        }
        // Exodus-end

        // Exodus: one sequential walk for cost + working factors; commit only after payment.
        // TransactionCost is the crate/machine fee shown in UI (cartBalance + cratecost).
        // Exodus: quote the cart and the actual delivery crate as one purchase.
        if (!TryQuoteMarketCart(liveCart, marketMod, component.CratePrototype,
                consoleComponent.TransactionCost, out var quote, out _))
        {
            _popup.PopupEntity(Loc.GetString("market-purchase-unavailable"), consoleUid, player);
            RefreshState(consoleUid, playerBank.Balance, marketMod, consoleComponent); // Exodus: invalidate the old payable quote.
            return;
        }

        var spawnCost = quote.TotalPrice;
        if (expectedPrice != spawnCost)
        {
            _popup.PopupEntity(Loc.GetString("market-purchase-price-changed"), consoleUid, player);
            RefreshState(consoleUid, playerBank.Balance, marketMod, consoleComponent);
            return;
        }
        if (playerBank.Balance < spawnCost)
            return;

        // Exodus-begin: validate and take the complete basket on the server thread before payment.
        if (!_marketInventory.TryTakeStock(liveCart, out var reserved))
        {
            _popup.PopupEntity(Loc.GetString("market-purchase-stock-changed"), consoleUid, player);
            RefreshState(consoleUid, playerBank.Balance, marketMod, consoleComponent);
            return;
        }
        // Exodus-end

        // Withdraw spesos from player
        if (!_bankSystem.TryBankWithdraw(player, spawnCost))
        {
            // Exodus: payment failure returns exactly the units that were taken, without a taxable refund.
            foreach (var entry in reserved)
            {
                if (!_marketInventory.TryAddStock(entry.Prototype, entry.Quantity, entry.Price, entry.StackPrototype))
                    Log.Error($"Unable to return {entry.Quantity} units of {entry.Prototype} after a failed market checkout.");
            }

            _popup.PopupEntity(Loc.GetString("market-insufficient-funds"), consoleUid, player);
            _audio.PlayPredicted(consoleComponent.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));
            return;
        }

        // Exodus: commit buy pressure only after successful payment (same tx as quoted cart cost).
        _dynamicMarket.CommitTransaction(quote.Transaction); // Exodus: paid composition, including the crate.

        _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

        itemSpawner.ItemsToSpawn = reserved; // Exodus: deliver the detached stock actually consumed by this payment.
        consoleComponent.CartDataList = [];
        _crateMachine.OpenFor(crateMachineUid, component);
        RefreshState(consoleUid, playerBank.Balance, marketMod, consoleComponent); // Exodus: publish the empty cart and remaining balance after checkout.
    }

    private void SpawnCrateItems(List<MarketData> spawnList, EntityUid targetCrate)
    {
        var coordinates = Transform(targetCrate).Coordinates;
        foreach (var data in spawnList)
        {
            if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                var entityList = _stackSystem.SpawnMultiple(stackPrototype.Spawn, data.Quantity, coordinates);
                foreach (var entity in entityList)
                {
                    _crateMachine.InsertIntoCrate(entity, targetCrate);
                }
            }
            else
            {
                // Spawn the requested quantity of non-stackable items
                for (int i = 0; i < data.Quantity; i++)
                {
                    var spawn = Spawn(data.Prototype); // Exodus: initialize structures away from the grid before packing.
                    _crateMachine.InsertIntoCrate(spawn, targetCrate);
                }
            }
        }
    }

    private void OnCrateMachineOpened(EntityUid uid, CrateMachineComponent component, CrateMachineOpenedEvent args)
    {
        if (!TryComp<MarketItemSpawnerComponent>(uid, out var itemSpawner))
            return;

        var targetCrate = _crateMachine.SpawnCrate(uid, component);
        SpawnCrateItems(itemSpawner.ItemsToSpawn, targetCrate);
        itemSpawner.ItemsToSpawn = [];
    }
}
