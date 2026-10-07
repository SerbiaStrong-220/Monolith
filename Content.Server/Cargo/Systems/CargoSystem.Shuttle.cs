using Content.Server.Cargo.Components;
using Content.Shared.Stacks;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Audio;
using Content.Shared.Whitelist; // Frontier
using Content.Server._NF.Cargo.Components; // Frontier
using Content.Shared._NF.Bank.Components; // Frontier
using Content.Shared.Mobs;
using Robust.Shared.Containers; // Frontier
using Content.Shared._Mono.ItemTax.Components; // Mono
using Content.Server._NF.Bank;
using Content.Server._NF.Trade; // Mono
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Trade;
using Content.Shared.Mech.Components;
using Robust.Shared.Toolshed.Commands.Math; // Mono
using Content.Server._Exodus.Economy; // Exodus dynamic market
using Content.Shared._Exodus.Economy; // Exodus CargoPalletAppraisalEntry
namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /*
     * Handles cargo shuttle / trade mechanics.
     */

    // Frontier addition:
    // The maximum distance from the console to look for pallets.
    private const int DefaultPalletDistance = 8;

    private static readonly SoundPathSpecifier ApproveSound = new("/Audio/Effects/Cargo/ping.ogg");

    private void InitializeShuttle()
    {
        SubscribeLocalEvent<TradeStationComponent, GridSplitEvent>(OnTradeSplit);

        SubscribeLocalEvent<CargoShuttleConsoleComponent, ComponentStartup>(OnCargoShuttleConsoleStartup);

        SubscribeLocalEvent<CargoPalletConsoleComponent, CargoPalletSellMessage>(OnPalletSale);
        SubscribeLocalEvent<CargoPalletConsoleComponent, CargoPalletAppraiseMessage>(OnPalletAppraise);
        SubscribeLocalEvent<CargoPalletConsoleComponent, BoundUIOpenedEvent>(OnPalletUIOpen);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    #region Console

    private void UpdateCargoShuttleConsoles(EntityUid shuttleUid, CargoShuttleComponent _)
    {
        // Update pilot consoles that are already open.
        _console.RefreshDroneConsoles();

        // Update order consoles.
        var shuttleConsoleQuery = AllEntityQuery<CargoShuttleConsoleComponent>();

        while (shuttleConsoleQuery.MoveNext(out var uid, out var _))
        {
            var stationUid = _station.GetOwningStation(uid);
            if (stationUid != shuttleUid)
                continue;

            UpdateShuttleState(uid, stationUid);
        }
    }

    private void UpdatePalletConsoleInterface(Entity<CargoPalletConsoleComponent> uid) // Frontier: EntityUid<Entity
    {
        if (Transform(uid).GridUid is not { Valid: true } gridUid)
        {
            _uiSystem.SetUiState(uid.Owner,
                CargoPalletConsoleUiKey.Sale, // Frontier: uid<uid.Owner
                new CargoPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        // Frontier: per-object market modification
        // Exodus: appraise / UI refresh — no market commit; also builds per-line listing for UI.
        var appraisalItems = new List<CargoPalletAppraisalEntry>(); // Exodus: only allocate display rows for appraisal.
        GetPalletGoods(uid, gridUid, out var toSell, out var amount, out var noModAmount, out _, out _, out _, out _, out _, appraisalItems);

        amount += noModAmount;
        // End Frontier

        // Monolith: display multiplier
        var station = _station.GetOwningStation(uid);
        var tradeCrateMultiplier = 1D;
        var otherMultiplier = 1D;

        if (TryComp<TradeCrateWildcardDestinationComponent>(station, out var wildcard))
            tradeCrateMultiplier = wildcard.ValueMultiplier;

        if (TryComp<MarketModifierComponent>(uid, out var marketModifier) && !marketModifier.Buy)
            otherMultiplier = marketModifier.Mod;

        _uiSystem.SetUiState(uid.Owner,
            CargoPalletConsoleUiKey.Sale, // Frontier: uid<uid.Owner
            new CargoPalletConsoleInterfaceState(DynamicMarketSystem.RoundSellPayout(amount), toSell.Count, true, tradeCrateMultiplier, otherMultiplier, appraisalItems)); // Exodus items
        // End Monolith
    }

    private void OnPalletUIOpen(EntityUid uid, CargoPalletConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdatePalletConsoleInterface((uid, component)); // Frontier: EntityUid<Entity
    }

    /// <summary>
    /// Ok so this is just the same thing as opening the UI, its a refresh button.
    /// I know this would probably feel better if it were like predicted and dynamic as pallet contents change
    /// However.
    /// I dont want it to explode if cargo uses a conveyor to move 8000 pineapple slices or whatever, they are
    /// known for their entity spam i wouldnt put it past them
    /// </summary>

    private void OnPalletAppraise(EntityUid uid, CargoPalletConsoleComponent component, CargoPalletAppraiseMessage args)
    {
        UpdatePalletConsoleInterface((uid, component)); // Frontier: EntityUid<Entity
    }

    private void OnCargoShuttleConsoleStartup(EntityUid uid, CargoShuttleConsoleComponent component, ComponentStartup args)
    {
        var station = _station.GetOwningStation(uid);
        UpdateShuttleState(uid, station);
    }

    private void UpdateShuttleState(EntityUid uid, EntityUid? station = null)
    {
        TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDatabase);
        TryComp<CargoShuttleComponent>(orderDatabase?.Shuttle, out var shuttle);

        var orders = GetProjectedOrders(uid, station ?? EntityUid.Invalid, orderDatabase, shuttle);
        var shuttleName = orderDatabase?.Shuttle != null ? MetaData(orderDatabase.Shuttle.Value).EntityName : string.Empty;

        if (_uiSystem.HasUi(uid, CargoConsoleUiKey.Shuttle))
            _uiSystem.SetUiState(uid, CargoConsoleUiKey.Shuttle, new CargoShuttleConsoleBoundUserInterfaceState(
                station != null ? MetaData(station.Value).EntityName : Loc.GetString("cargo-shuttle-console-station-unknown"),
                string.IsNullOrEmpty(shuttleName) ? Loc.GetString("cargo-shuttle-console-shuttle-not-found") : shuttleName,
                orders
            ));
    }

    #endregion

    private void OnTradeSplit(EntityUid uid, TradeStationComponent component, ref GridSplitEvent args)
    {
        // If the trade station gets bombed it's still a trade station.
        foreach (var gridUid in args.NewGrids)
        {
            EnsureComp<TradeStationComponent>(gridUid);
        }
    }

    #region Shuttle

    /// <summary>
    /// Returns the orders that can fit on the cargo shuttle.
    /// </summary>
    private List<CargoOrderData> GetProjectedOrders(
        EntityUid consoleUid,
        EntityUid shuttleUid,
        StationCargoOrderDatabaseComponent? component = null,
        CargoShuttleComponent? shuttle = null)
    {
        var orders = new List<CargoOrderData>();

        if (component == null || shuttle == null || component.Orders.Count == 0)
            return orders;

        var spaceRemaining = GetCargoSpace(consoleUid, shuttleUid);
        for (var i = 0; i < component.Orders.Count && spaceRemaining > 0; i++)
        {
            var order = component.Orders[i];
            if (order.Approved)
            {
                var numToShip = order.OrderQuantity - order.NumDispatched;
                if (numToShip > spaceRemaining)
                {
                    // We won't be able to fit the whole order on, so make one
                    // which represents the space we do have left:
                    var reducedOrder = new CargoOrderData(order.OrderId,
                            order.ProductId, order.ProductName, order.Price, spaceRemaining, order.Requester, order.Reason, null);
                    orders.Add(reducedOrder);
                }
                else
                {
                    orders.Add(order);
                }
                spaceRemaining -= numToShip;
            }
        }

        return orders;
    }

    /// <summary>
    /// Get the amount of space the cargo shuttle can fit for orders.
    /// </summary>
    private int GetCargoSpace(EntityUid consoleUid, EntityUid gridUid)
    {
        var space = GetCargoPallets(consoleUid, gridUid, BuySellType.Buy).Count;
        return space;
    }

    /// <summary>
    /// Frontier addition - calculates distance between two EntityCoordinates
    /// Used to check for cargo pallets around the console instead of on the grid.
    /// </summary>
    /// <param name="point1">first point to get distance between</param>
    /// <param name="point2">second point to get distance between</param>
    /// <returns></returns>
    public static double CalculateDistance(EntityCoordinates point1, EntityCoordinates point2)
    {
        var xDifference = point2.X - point1.X;
        var yDifference = point2.Y - point1.Y;

        return Math.Sqrt(xDifference * xDifference + yDifference * yDifference);
    }

    /// GetCargoPallets(gridUid, BuySellType.Sell) to return only Sell pads
    /// GetCargoPallets(gridUid, BuySellType.Buy) to return only Buy pads
    private List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent PalletXform)> GetCargoPallets(EntityUid consoleUid, EntityUid gridUid, BuySellType requestType = BuySellType.All)
    {
        // Exodus-begin: query nearby pallets instead of scanning every grid's pallets.
        _pads.Clear();
        _palletLookup.Clear();
        var coordinates = _transformSystem.WithEntityId(Transform(consoleUid).Coordinates, gridUid);
        var maxDistance = TryComp<CargoPalletConsoleComponent>(consoleUid, out var console)
            ? console.PalletDistance
            : DefaultPalletDistance;
        if (maxDistance < 0)
            return _pads;

        _lookup.GetEntitiesInRange(coordinates, Math.Max(0.01f, maxDistance), _palletLookup, LookupFlags.StaticSundries);
        var maxDistanceSquared = (double) maxDistance * maxDistance;

        foreach (var (uid, comp) in _palletLookup)
        {
            if (!_xformQuery.TryGetComponent(uid, out var compXform) ||
                compXform.ParentUid != gridUid ||
                !compXform.Anchored ||
                (compXform.LocalPosition - coordinates.Position).LengthSquared() > maxDistanceSquared)
            {
                continue;
            }

            if ((requestType & comp.PalletType) == 0)
            {
                continue;
            }

            _pads.Add((uid, comp, compXform));
        }

        return _pads;
        // Exodus-end
    }

    private List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)>
        GetFreeCargoPallets(EntityUid gridUid,
            List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)> pallets)
    {
        _setEnts.Clear();

        List<(EntityUid Entity, CargoPalletComponent Component, TransformComponent Transform)> outList = new();

        foreach (var pallet in pallets)
        {
            var aabb = _lookup.GetAABBNoContainer(pallet.Entity, pallet.Transform.LocalPosition, pallet.Transform.LocalRotation);

            if (_lookup.AnyLocalEntitiesIntersecting(gridUid, aabb, LookupFlags.Dynamic))
                continue;

            outList.Add(pallet);
        }

        return outList;
    }

    #endregion

    #region Station

    private bool SellPallets(Entity<CargoPalletConsoleComponent> consoleUid, EntityUid gridUid, out double amount, out double noMultiplierAmount, out double blackMarketTaxAmount, out double frontierTaxAmount, out double nfsdTaxAmount, out double medicalTaxAmount) // Frontier: first arg to Entity, add noMultiplierAmount
    {
        // Exodus-begin: wait for persisted market settings before accepting goods.
        amount = noMultiplierAmount = blackMarketTaxAmount = frontierTaxAmount = nfsdTaxAmount = medicalTaxAmount = 0;
        if (!_dynamicMarket.Ready)
            return false;
        // Exodus-end

        // Exodus: price with applyImpact=false; commit market only after a real non-empty sale.
        var marketImpactAppliedRoots = new HashSet<EntityUid>(); // Exodus: identify roots covered by the committed market transaction.
        GetPalletGoods(consoleUid, gridUid, out var toSell, out amount, out noMultiplierAmount, out blackMarketTaxAmount, out frontierTaxAmount, out nfsdTaxAmount, out medicalTaxAmount, out var marketTx,
            marketImpactAppliedRoots: marketImpactAppliedRoots); // Frontier + Exodus

        Log.Debug($"Cargo sold {toSell.Count} entities for {amount} (plus {noMultiplierAmount} without mods). (Taxes: Black Market: {blackMarketTaxAmount}, CO: {frontierTaxAmount}, TSFMC: {nfsdTaxAmount}, MD: {medicalTaxAmount})"); // Frontier: add section in parentheses

        if (toSell.Count == 0)
            return false;

        _dynamicMarket.CommitTransaction(marketTx); // Exodus: sell pressure only on confirmed sale (not appraise)

        var ev = new EntitySoldEvent(toSell, gridUid, marketImpactAppliedRoots); // Exodus: preserve per-root pressure ownership.
        RaiseLocalEvent(ref ev);

        // Collect all container entities and their contained entities recursively
        var allEntsToDelete = new HashSet<EntityUid>(toSell);

        // Make sure we delete all contained entities as well
        foreach (var ent in toSell)
        {
            if (TryComp<ContainerManagerComponent>(ent, out var containerManager))
            {
                // Recursively gather all entities inside containers
                var containedEntities = new HashSet<EntityUid>();
                GatherContainedEntities(ent, containerManager, containedEntities);
                allEntsToDelete.UnionWith(containedEntities);
            }
        }

        foreach (var ent in allEntsToDelete)
        {
            Del(ent);
        }

        return true;
    }

    /// <summary>
    /// Recursively gathers all entities inside containers
    /// </summary>
    private void GatherContainedEntities(EntityUid uid, ContainerManagerComponent containerManager, HashSet<EntityUid> containedEntities)
    {
        foreach (var container in containerManager.Containers.Values)
        {
            foreach (var entity in container.ContainedEntities)
            {
                containedEntities.Add(entity);

                // Recursively check containers inside this entity
                if (TryComp<ContainerManagerComponent>(entity, out var nestedContainers))
                {
                    GatherContainedEntities(entity, nestedContainers, containedEntities);
                }
            }
        }
    }

    // Exodus: market-aware pallet valuation is in CargoSystem.PalletMarket.Exodus.cs.

    private bool CanSell(EntityUid uid, TransformComponent xform)
    {
        // Frontier: Look for blacklisted items and stop the selling of the container.
        if (_blacklistQuery.HasComponent(uid))
            return false;

        // Frontier: allow selling dead mobs, Mono: and mecha
        if (_mobQuery.TryComp(uid, out var mob) && mob.CurrentState != MobState.Dead && !TryComp<MechComponent>(uid, out _) &&
            !CanSellLivingMob(uid)) // Exodus: explicitly saleable NPC equipment may remain operational.
            return false;
        // End Frontier

        var complete = IsBountyComplete(uid, out var bountyEntities);

        // Recursively check for mobs at any point.
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (complete && bountyEntities.Contains(child))
                continue;

            if (!CanSell(child, _xformQuery.GetComponent(child)))
                return false;
        }

        return true;
    }

    private void OnPalletSale(EntityUid uid, CargoPalletConsoleComponent component, CargoPalletSellMessage args)
    {
        var xform = Transform(uid);

        if (xform.GridUid is not { Valid: true } gridUid)
        {
            _uiSystem.SetUiState(uid, CargoPalletConsoleUiKey.Sale,
            new CargoPalletConsoleInterfaceState(0, 0, false));
            return;
        }

        if (!SellPallets((uid, component), gridUid, out var price, out var noMultiplierPrice, out var blackMarketTaxAmount, out var frontierTaxAmount, out var nfsdTaxAmount, out var medicalTaxAmount)) // Frontier: convert first arg to Entity, add noMultiplierPrice
            return;

        price += noMultiplierPrice;

        // End Frontier: market modifiers & immune objects
        // Mono Begin
        if (blackMarketTaxAmount > 0)
            _bank.TrySectorDeposit(SectorBankAccount.BlackMarket, DynamicMarketSystem.RoundSellPayout(blackMarketTaxAmount), LedgerEntryType.BlackMarketSales); // Exodus: bounded economy payout
        if (frontierTaxAmount > 0)
            _bank.TrySectorDeposit(SectorBankAccount.Frontier, DynamicMarketSystem.RoundSellPayout(frontierTaxAmount), LedgerEntryType.ColonialOutpostSales); // Exodus: bounded economy payout
        if (nfsdTaxAmount > 0)
            _bank.TrySectorDeposit(SectorBankAccount.Nfsd, DynamicMarketSystem.RoundSellPayout(nfsdTaxAmount), LedgerEntryType.TSFMCSales); // Exodus: bounded economy payout
        if (medicalTaxAmount > 0)
            _bank.TrySectorDeposit(SectorBankAccount.Medical, DynamicMarketSystem.RoundSellPayout(medicalTaxAmount), LedgerEntryType.MedicalSales); // Exodus: bounded economy payout
        if (blackMarketTaxAmount < 0)
        {
            blackMarketTaxAmount = -blackMarketTaxAmount;
            _bank.TrySectorWithdraw(SectorBankAccount.BlackMarket, DynamicMarketSystem.RoundSellPayout(blackMarketTaxAmount), LedgerEntryType.BlackMarketPenalties); // Exodus: bounded economy payout
        }
        if (frontierTaxAmount < 0)
        {
            frontierTaxAmount = -frontierTaxAmount;
            _bank.TrySectorWithdraw(SectorBankAccount.Frontier, DynamicMarketSystem.RoundSellPayout(frontierTaxAmount), LedgerEntryType.ColonialOutpostPenalties); // Exodus: bounded economy payout
        }
        if (nfsdTaxAmount < 0)
        {
            nfsdTaxAmount = -nfsdTaxAmount;
            _bank.TrySectorWithdraw(SectorBankAccount.Nfsd, DynamicMarketSystem.RoundSellPayout(nfsdTaxAmount), LedgerEntryType.TSFMCPenalties); // Exodus: bounded economy payout
        }
        if (medicalTaxAmount < 0)
        {
            medicalTaxAmount = -medicalTaxAmount;
            _bank.TrySectorWithdraw(SectorBankAccount.Medical, DynamicMarketSystem.RoundSellPayout(medicalTaxAmount), LedgerEntryType.MedicalPenalties); // Exodus: bounded economy payout
        }
        // Mono End
        var stackPrototype = _protoMan.Index<StackPrototype>(component.CashType);
        _stack.Spawn(DynamicMarketSystem.RoundSellPayout(price), stackPrototype, xform.Coordinates); // Exodus: saturate oversized payout
        _audio.PlayPvs(ApproveSound, uid);
        UpdatePalletConsoleInterface((uid, component)); // Frontier: EntityUid<Entity
    }

    #endregion

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Reset();
        CleanupTradeCrateDestinations(); // Frontier
    }
}

/// <summary>
/// Event broadcast raised by-ref before it is sold and
/// deleted but after the price has been calculated.
/// </summary>
[ByRefEvent]
// Exodus: null identifies endpoints whose goods have not contributed to a market transaction yet.
public readonly record struct EntitySoldEvent(
    HashSet<EntityUid> Sold,
    EntityUid Grid,
    IReadOnlySet<EntityUid>? MarketImpactAppliedRoots = null);
