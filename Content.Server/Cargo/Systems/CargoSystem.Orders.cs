using System.Diagnostics.CodeAnalysis;
using Content.Server._NF.Bank; // Frontier
using Content.Server.Cargo.Components;
using Content.Server.Labels.Components;
using Content.Shared._NF.Bank.Components; // Frontier
using Content.Server.Station.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared._NF.Bank.BUI; // Frontier
using Content.Server._Exodus.Economy; // Exodus dynamic market
using Content.Shared._NF.Market; // Exodus resale reservation
using Content.Shared._Exodus.Economy; // Exodus CargoMarketListing
using Content.Shared.Stacks; // Exodus market lot sizing
using Content.Server._Exodus.Cargo; // Exodus bulk cargo packaging

namespace Content.Server.Cargo.Systems
{
    public sealed partial class CargoSystem
    {
        [Dependency] private SharedTransformSystem _transformSystem = default!;
        [Dependency] private EmagSystem _emag = default!;

        /// <summary>
        /// How much time to wait (in seconds) before increasing bank accounts balance.
        /// </summary>
        private const int Delay = 10;

        /// <summary>
        /// Keeps track of how much time has elapsed since last balance increase.
        /// </summary>
        private float _timer;

        private void InitializeConsole()
        {
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleAddOrderMessage>(OnAddOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleRemoveOrderMessage>(OnRemoveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleApproveOrderMessage>(OnApproveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, BoundUIOpenedEvent>(OnOrderUIOpened);
            SubscribeLocalEvent<CargoOrderConsoleComponent, ComponentInit>(OnInit);
            //SubscribeLocalEvent<CargoOrderConsoleComponent, InteractUsingEvent>(OnInteractUsing); //Frontier Disabled
            SubscribeLocalEvent<CargoOrderConsoleComponent, BankBalanceUpdatedEvent>(OnOrderBalanceUpdated);
            SubscribeLocalEvent<CargoOrderConsoleComponent, GotEmaggedEvent>(OnEmagged);
            SubscribeLocalEvent<CargoOrderConsoleComponent, GotUnEmaggedEvent>(OnUnemagged); // Frontier
            Reset();
        }

        // Frontier: disabled
        /*
        private void OnInteractUsing(EntityUid uid, CargoOrderConsoleComponent component, ref InteractUsingEvent args)
        {
            if (!HasComp<CashComponent>(args.Used))
                return;

            var price = _pricing.GetPrice(args.Used);

            if (price == 0)
                return;

            var stationUid = _station.GetOwningStation(args.Used);

            if (!TryComp(stationUid, out StationBankAccountComponent? bank))
                return;

            _audio.PlayPvs(component.ConfirmSound, uid);
            UpdateBankAccount(stationUid.Value, bank, (int) price);
            QueueDel(args.Used);
            args.Handled = true;
        }
        */
        // End Frontier

        private void OnInit(EntityUid uid, CargoOrderConsoleComponent orderConsole, ComponentInit args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, orderConsole, station);
        }

        private void Reset()
        {
            _timer = 0;
        }

        private void OnEmagged(Entity<CargoOrderConsoleComponent> ent, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(ent, EmagType.Interaction))
                return;

            args.Handled = true;
        }

        // Frontier: demag
        private void OnUnemagged(Entity<CargoOrderConsoleComponent> ent, ref GotUnEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (!_emag.CheckFlag(ent, EmagType.Interaction))
                return;

            args.Handled = true;
        }
        // End Frontier: demag

        private void UpdateConsole(float frameTime)
        {
            _timer += frameTime;

            // TODO: Doesn't work with serialization and shouldn't just be updating every delay
            // client can just interp this just fine on its own.
            while (_timer > Delay)
            {
                _timer -= Delay;

                var stationQuery = EntityQueryEnumerator<StationBankAccountComponent>();
                while (stationQuery.MoveNext(out var uid, out var bank))
                {
                    var balanceToAdd = bank.IncreasePerSecond * Delay;
                    UpdateBankAccount(uid, bank, balanceToAdd);
                }

                var query = EntityQueryEnumerator<CargoOrderConsoleComponent>();
                while (query.MoveNext(out var uid, out var comp))
                {
                    if (!_uiSystem.IsUiOpen(uid, CargoConsoleUiKey.Orders)) continue;

                    var station = _station.GetOwningStation(uid);
                    UpdateOrderState(uid, comp, station);
                }
            }
        }

        #region Interface

        private void OnApproveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleApproveOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (!_accessReaderSystem.IsAllowed(player, uid))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-order-not-allowed"));
                PlayDenySound(uid, component);
                return;
            }

            var station = _station.GetOwningStation(uid);

            // Frontier: orders require a bank account.
            if (!TryComp<BankAccountComponent>(player, out var bankAccount))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-nf-no-bank-account"));
                PlayDenySound(uid, component);
                return;
            }

            // No station to deduct from.
            // Frontier: no checks for StationData/StationBankAccount.
            if (station == null
                || !TryGetOrderDatabase(uid, out var dbUid, out var orderDatabase, component))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                PlayDenySound(uid, component);
                return;
            }

            // Find our order again. It might have been dispatched or approved already
            var order = orderDatabase.Orders.Find(order => args.OrderId == order.OrderId && !order.Approved);
            if (order == null)
            {
                return;
            }

            // Invalid order
            if (!_protoMan.HasIndex<EntityPrototype>(order.ProductId))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                PlayDenySound(uid, component);
                return;
            }

            var amount = GetOutstandingOrderCount(orderDatabase);
            var capacity = orderDatabase.Capacity;

            // Too many orders, avoid them getting spammed in the UI.
            if (amount >= capacity)
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-too-many"));
                PlayDenySound(uid, component);
                return;
            }

            // Cap orders so someone can't spam thousands.
            var cappedAmount = Math.Min(capacity - amount, order.OrderQuantity);

            if (cappedAmount != order.OrderQuantity)
            {
                order.OrderQuantity = cappedAmount;
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-snip-snip"));
                PlayDenySound(uid, component);
            }

            // Exodus: resale stock — cannot approve more than remaining station market stock
            if (order.FromResaleStock)
            {
                // Exodus: an old request never silently purchases fewer shared units.
                if (!_market.TryGetStock(station.Value, order.ProductId, out var stock) ||
                    order.OrderQuantity <= 0 || order.OrderQuantity > stock.Quantity)
                {
                    order.TotalPrice = null; // Exodus: invalidate the stale payable quote.
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-resale-out-of-stock"));
                    PlayDenySound(uid, component);
                    UpdateOrders(station.Value); // Exodus: show the unavailable order and current shared stock.
                    return;
                }

                order.Price = stock.Price; // Exodus: approve against the current stock appraisal, not an old request.
            }

            // Exodus-begin: sequential buy lots × global sector factor; optional local MarketModifier on console
            if (!TryQuoteCargoOrder(uid, order, out var marketQuote))
            {
                order.TotalPrice = null;
                ConsolePopup(args.Actor, Loc.GetString("market-purchase-unavailable"));
                UpdateOrders(station.Value);
                return;
            }

            var cost = marketQuote.TotalPrice;
            if (args.ExpectedPrice != cost)
            {
                order.TotalPrice = cost;
                ConsolePopup(args.Actor, Loc.GetString("market-purchase-price-changed"));
                UpdateOrders(station.Value);
                return;
            }
            // Exodus-end

            // Not enough balance
            if (!_bank.TryBankWithdraw(player, cost, dry: true)) // Exodus: Correct bank check
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                PlayDenySound(uid, component);
                return;
            }

            // Frontier: no cargo fulfillment check
            //var ev = new FulfillCargoOrderEvent((station.Value, stationData), order, (uid, component));
            //RaiseLocalEvent(ref ev); // Frontier
            //ev.FulfillmentEntity ??= station.Value; // Frontier

            // if (!ev.Handled)
            // {
            //     ev.FulfillmentEntity = TryFulfillOrder((station.Value, stationData), order, orderDatabase);

            //     if (ev.FulfillmentEntity == null)
            //     {
            //         ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
            //         PlayDenySound(uid, component);
            //         return;
            //     }
            // }
            // End Frontier

            // Exodus: reserve stock before charging; rollback stock rather than making a taxable refund.
            MarketData? reservedStock = null;
            if (order.FromResaleStock &&
                !_market.TryTakeStock(station.Value, order.ProductId, order.OrderQuantity, out reservedStock))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-resale-out-of-stock"));
                PlayDenySound(uid, component);
                return;
            }

            if (!_bank.TryBankWithdraw(player, cost))
            {
                if (reservedStock != null)
                    _market.ReturnStock(station.Value, reservedStock);

                ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                PlayDenySound(uid, component);
                return;
            }

            _dynamicMarket.CommitTransaction(marketQuote.Transaction); // Exodus: commit the paid composition exactly once.
            order.TotalPrice = cost; // Exodus: retain the amount actually paid in the approved order.

            order.Approved = true;
            _audio.PlayPvs(component.ConfirmSound, uid);

            if (!_emag.CheckFlag(uid, EmagType.Interaction))
            {
                var tryGetIdentityShortInfoEvent = new TryGetIdentityShortInfoEvent(uid, player);
                RaiseLocalEvent(tryGetIdentityShortInfoEvent);
                order.SetApproverData(tryGetIdentityShortInfoEvent.Title);

                // Frontier: silence cargo station
                // var message = Loc.GetString("cargo-console-unlock-approved-order-broadcast",
                //     ("productName", Loc.GetString(order.ProductName)),
                //     ("orderAmount", order.OrderQuantity),
                //     ("approver", order.Approver ?? string.Empty),
                //     ("cost", cost));
                // _radio.SendRadioMessage(uid, message, component.AnnouncementChannel, uid, escapeMarkup: false);
                // End Frontier: silence cargo station
            }

            ConsolePopup(args.Actor, Loc.GetString("cargo-console-trade-station", ("destination", MetaData(uid).EntityName))); // ev.FulfillmentEntity.Value<uid

            // Log order approval
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):user} approved order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.ProductId}, requester:{order.Requester}, reason:{order.Reason}] with balance at {bankAccount.Balance}");

            // orderDatabase.Orders.Remove(order); // Frontier

            // Frontier: account balances, taxing vendor purchases
            foreach (var (account, taxCoeff) in component.TaxAccounts)
            {
                if (!float.IsFinite(taxCoeff) || taxCoeff <= 0.0f)
                    continue;
                var tax = DynamicMarketSystem.RoundToPrice(Math.Floor(marketQuote.NominalPrice * (double)taxCoeff)); // Exodus: the price-floor adjustment is never distributed.
                _bank.TrySectorDeposit(account, tax, LedgerEntryType.CargoTax);
            }
            // End Frontier

            UpdateOrders(station.Value);
        }

        // Frontier - consoleUid is required to find cargo pads
        // Only consoleUid is added thats the frontier change
        private EntityUid? TryFulfillOrder(EntityUid consoleUid, StationDataComponent stationData, CargoOrderData order, StationCargoOrderDatabaseComponent orderDatabase)
        {
            // No slots at the trade station
            _listEnts.Clear();
            GetTradeStations(stationData, ref _listEnts);
            EntityUid? tradeDestination = null;

            // Try to fulfill from any station where possible, if the pad is not occupied.
            foreach (var trade in _listEnts)
            {
                var tradePads = GetCargoPallets(consoleUid, trade, BuySellType.Buy);
                _random.Shuffle(tradePads);

                var freePads = GetFreeCargoPallets(trade, tradePads);
                if (freePads.Count >= order.OrderQuantity) //check if the station has enough free pallets
                {
                    foreach (var pad in freePads)
                    {
                        var coordinates = new EntityCoordinates(trade, pad.Transform.LocalPosition);

                        var delivered = FulfillOrder(order, coordinates, orderDatabase.PrinterOutput); // Exodus: count actual deliveries.
                        if (delivered > 0)
                        {
                            tradeDestination = trade;
                            order.NumDispatched += delivered; // Exodus
                            if (order.OrderQuantity <= order.NumDispatched) //Spawn a crate on free pellets until the order is fulfilled.
                                break;
                        }
                    }
                }

                if (tradeDestination != null)
                    break;
            }

            return tradeDestination;
        }

        private void GetTradeStations(StationDataComponent data, ref List<EntityUid> ents)
        {
            foreach (var gridUid in data.Grids)
            {
                if (!_tradeQuery.HasComponent(gridUid))
                    continue;

                ents.Add(gridUid);
            }
        }

        private void OnRemoveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleRemoveOrderMessage args)
        {
            if (!TryGetOrderDatabase(uid, out var dbUid, out var orderDatabase, component))
                return;

            RemoveOrder(dbUid!.Value, args.OrderId, orderDatabase);
        }

        private void OnAddOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (args.Amount <= 0)
                return;

            var bank = GetBankAccount(uid, component);

            if (!HasComp<BankAccountComponent>(player) && bank == null) return;


            if (!TryGetOrderDatabase(uid, out var dbUid, out var orderDatabase, component))
                return;

            // Exodus-begin: resale stock from sold goods (not YAML cargo catalog)
            if (CargoMarketListing.TryParseResaleId(args.CargoProductId, out var resaleEntityId))
            {
                TryAddResaleOrder((uid, component), player, (dbUid!.Value, orderDatabase), args, resaleEntityId);
                return;
            }
            // Exodus-end

            if (!_protoMan.TryIndex<CargoProductPrototype>(args.CargoProductId, out var product))
            {
                Log.Error($"Tried to add invalid cargo product {args.CargoProductId} as order!");
                return;
            }

            if (!component.AllowedGroups.Contains(product.Group))
                return;

            var data = GetOrderData(EntityManager.GetNetEntity(uid), args, product, GenerateOrderId(orderDatabase));
            // Exodus: reject unsupported or unrepresentable purchases before creating an order.
            if (!TryQuoteCargoOrder(uid, data, out var purchaseQuote))
            {
                ConsolePopup(player, Loc.GetString("market-purchase-unavailable"));
                return;
            }

            data.TotalPrice = purchaseQuote.TotalPrice;

            if (!TryAddOrder(orderDatabase.Owner, data, orderDatabase))
            {
                PlayDenySound(uid, component);
                return;
            }

            // Log order addition
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(player):user} added order [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.ProductId}, requester:{data.Requester}, reason:{data.Reason}]");

        }

        private void OnOrderUIOpened(EntityUid uid, CargoOrderConsoleComponent component, BoundUIOpenedEvent args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, component, station);
        }

        #endregion


        private void OnOrderBalanceUpdated(Entity<CargoOrderConsoleComponent> ent, ref BankBalanceUpdatedEvent args)
        {
            if (!_uiSystem.IsUiOpen(ent.Owner, CargoConsoleUiKey.Orders))
                return;

            UpdateOrderState(ent, ent.Comp, args.Station); // Frontier: add ent.Comp
        }

        // Frontier: custom UpdateOrderState function
        // Exodus: reuse a catalog and sale ceiling only within the current shared inventory notification.
        private void UpdateOrderState(EntityUid uid, CargoOrderConsoleComponent component, EntityUid? station,
            List<CargoMarketListing>? marketListings = null, MarketSellCeiling? marketCeiling = null)
        {
            var uiUsers = _uiSystem.GetActors((uid, null), CargoConsoleUiKey.Orders);
            foreach (var user in uiUsers)
            {
                var balance = 0;

                if (Transform(user).GridUid is EntityUid stationGrid &&
                    TryComp<BankAccountComponent>(user, out var playerBank))
                {
                    station = stationGrid;
                    balance = playerBank.Balance;
                }
                else if (TryComp<StationBankAccountComponent>(station, out var stationBank))
                {
                    balance = stationBank.Balance;
                }

                if (station == null || !TryGetOrderDatabase(station.Value, out var orderStation, out var orderDatabase, component)) // Exodus: use the owning station for resale prices.
                    return;

                // Frontier - we only want to see orders made on the same computer, so filter them out
                var filteredOrders = orderDatabase.Orders
                    .Where(order => order.Computer == EntityManager.GetNetEntity(uid)).ToList();

                // Exodus: pending orders show a live quote; approved orders retain their paid total.
                marketCeiling ??= _marketSellCeilings.GetSnapshot(); // Exodus
                foreach (var order in filteredOrders)
                {
                    if (!order.Approved)
                    {
                        // Exodus-begin: a pending shared-stock order is payable only while all units remain available.
                        if (order.FromResaleStock)
                        {
                            if (orderStation == null ||
                                !_market.TryGetStock(orderStation.Value, order.ProductId, out var stock) ||
                                order.OrderQuantity <= 0 || order.OrderQuantity > stock.Quantity)
                            {
                                order.TotalPrice = null;
                                continue;
                            }

                            order.Price = stock.Price;
                        }
                        // Exodus-end

                        order.TotalPrice = TryQuoteCargoOrder(uid, order, out var purchaseQuote, marketCeiling)
                            ? purchaseQuote.TotalPrice
                            : null; // Exodus: unavailable prices must not fall back to an unsafe legacy estimate.
                    }
                }

                // Exodus-begin: live catalog listings from global sector market
                marketListings ??= BuildCargoMarketListings((uid, component), marketCeiling);
                var state = new CargoConsoleInterfaceState(
                    MetaData(user).EntityName,
                    GetOutstandingOrderCount(orderDatabase),
                    orderDatabase.Capacity,
                    balance,
                    filteredOrders,
                    marketListings);
                // Exodus-end

                _uiSystem.SetUiState(uid, CargoConsoleUiKey.Orders, state);
            }
        }
        // End Frontier

        private void ConsolePopup(EntityUid actor, string text)
        {
            _popup.PopupCursor(text, actor);
        }

        private void PlayDenySound(EntityUid uid, CargoOrderConsoleComponent component)
        {
            _audio.PlayPvs(_audio.ResolveSound(component.ErrorSound), uid);
        }

        private static CargoOrderData GetOrderData(NetEntity consoleUid, CargoConsoleAddOrderMessage args, CargoProductPrototype cargoProduct, int id)
        {
            return new CargoOrderData(id, cargoProduct.Product, cargoProduct.Name, cargoProduct.Cost, args.Amount, args.Requester, args.Reason, consoleUid);
        }

        public static int GetOutstandingOrderCount(StationCargoOrderDatabaseComponent component)
        {
            var amount = 0;

            foreach (var order in component.Orders)
            {
                if (!order.Approved)
                    continue;
                amount += order.OrderQuantity - order.NumDispatched;
            }

            return amount;
        }

        /// <summary>
        /// Updates all of the cargo-related consoles for a particular station.
        /// This should be called whenever orders change.
        /// </summary>
        private void UpdateOrders(EntityUid dbUid)
        {
            // Order added so all consoles need updating.
            var orderQuery = AllEntityQuery<CargoOrderConsoleComponent>();

            while (orderQuery.MoveNext(out var uid, out var comp))
            {
                var station = _station.GetOwningStation(uid);
                if (station != dbUid)
                    continue;

                UpdateOrderState(uid, comp, station);
            }

            var consoleQuery = AllEntityQuery<CargoShuttleConsoleComponent>();
            while (consoleQuery.MoveNext(out var uid, out var _))
            {
                var station = _station.GetOwningStation(uid);
                if (station != dbUid)
                    continue;

                UpdateShuttleState(uid, station);
            }
        }

        public bool AddAndApproveOrder(
            EntityUid dbUid,
            string spawnId,
            string name,
            int cost,
            int qty,
            string sender,
            string description,
            string dest,
            StationCargoOrderDatabaseComponent component,
            StationDataComponent stationData
        )
        {
            DebugTools.Assert(_protoMan.HasIndex<EntityPrototype>(spawnId));
            // Make an order
            var id = GenerateOrderId(component);
            var order = new CargoOrderData(id, spawnId, name, cost, qty, sender, description, null);

            // Approve it now
            order.SetApproverData(dest, sender);
            order.Approved = true;

            // Log order addition
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"AddAndApproveOrder {description} added order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.ProductId}, requester:{order.Requester}, reason:{order.Reason}]");

            // Add it to the list
            return TryAddOrder(dbUid, order, component);
        }

        private bool TryAddOrder(EntityUid dbUid, CargoOrderData data, StationCargoOrderDatabaseComponent component)
        {
            component.Orders.Add(data);
            UpdateOrders(dbUid);
            return true;
        }

        private static int GenerateOrderId(StationCargoOrderDatabaseComponent orderDB)
        {
            // We need an arbitrary unique ID to identify orders, since they may
            // want to be cancelled later.
            return ++orderDB.NumOrdersCreated;
        }

        public void RemoveOrder(EntityUid dbUid, int index, StationCargoOrderDatabaseComponent orderDB)
        {
            // Exodus: paid goods remain queued until delivery; client cancellation cannot discard or return them.
            var sequenceIdx = orderDB.Orders.FindIndex(order => order.OrderId == index && !order.Approved);
            if (sequenceIdx != -1)
            {
                orderDB.Orders.RemoveAt(sequenceIdx);
            }
            UpdateOrders(dbUid);
        }

        public void ClearOrders(StationCargoOrderDatabaseComponent component)
        {
            if (component.Orders.Count == 0)
                return;

            component.Orders.Clear();
        }

        // Exodus-begin: choose an order without consuming it before delivery succeeds.
        private bool TryGetNextOrder(List<NetEntity> consoleUidList, StationCargoOrderDatabaseComponent orderDB, [NotNullWhen(true)] out CargoOrderData? orderOut)
        {
            foreach (var order in orderDB.Orders)
            {
                if (!order.Approved || order.NumDispatched >= order.OrderQuantity ||
                    order.Computer is not { } console || !consoleUidList.Contains(console) ||
                    !_protoMan.HasIndex<EntityPrototype>(order.ProductId))
                    continue;

                orderOut = order;
                return true;
            }

            orderOut = null;
            return false;
        }
        // Exodus-end

        /// <summary>
        /// Tries to fulfill the next outstanding order.
        /// </summary>
        private bool FulfillNextOrder(List<NetEntity> consoleUidList, StationCargoOrderDatabaseComponent orderDB, EntityCoordinates spawn, string? paperProto)
        {
            if (!TryGetNextOrder(consoleUidList, orderDB, out var order)) // Exodus: select without dequeuing.
                return false;

            // Exodus-begin: one crate can fulfill multiple paid order units.
            var delivered = FulfillOrder(order, spawn, paperProto, orderDB.BulkPackaging);
            if (delivered <= 0)
                return false;

            order.NumDispatched += delivered;
            if (order.NumDispatched >= order.OrderQuantity)
                orderDB.Orders.Remove(order);

            return true;
            // Exodus-end
        }

        /// <summary>
        /// Fulfills the specified cargo order and spawns paper attached to it.
        /// </summary>
        // Exodus: return the actual shipped quantity, including bulk packages.
        private int FulfillOrder(CargoOrderData order, EntityCoordinates spawn, string? paperProto,
            CargoOrderPackagingSettings? packaging = null)
        {
            // Exodus: invalid/stale orders must stay queued rather than lose paid goods.
            if (order.NumDispatched >= order.OrderQuantity || !_protoMan.HasIndex<EntityPrototype>(order.ProductId))
                return 0;

            // Exodus: initialize packaged structures in nullspace so they never anchor to the delivery floor.
            var requiresPackaging = _packaging.RequiresPackaging(order.ProductId);
            var item = requiresPackaging ? Spawn(order.ProductId) : Spawn(order.ProductId, spawn);

            // Exodus: resale inventory counts individual units even if a prototype spawns a full stack.
            if (order.FromResaleStock && TryComp<StackComponent>(item, out var stack))
                _stack.SetCount(item, 1, stack);

            // Ensure the item doesn't start anchored
            _transformSystem.Unanchor(item, Transform(item));

            // Exodus-begin: attach one manifest to the package, retaining the purchased item's name.
            var itemName = MetaData(item).EntityName;
            var quantity = 1;
            if ((packaging != null || requiresPackaging) &&
                _packaging.TryPackOrder(item, order, packaging ?? new CargoOrderPackagingSettings(), spawn,
                    out var package, out var packed))
            {
                item = package;
                quantity = packed;
            }
            else if (requiresPackaging)
            {
                QueueDel(item);
                return 0;
            }
            // Exodus-end

            // Create a sheet of paper to write the order details on
            var printed = EntityManager.SpawnEntity(paperProto, spawn);
            EnsureComp<MarketReceiptComponent>(printed); // Exodus: issued invoices cannot be farmed by splitting cheap orders.
            if (TryComp<PaperComponent>(printed, out var paper))
            {
                // fill in the order data
                var val = Loc.GetString("cargo-console-paper-print-name", ("orderNumber", order.OrderId));
                _metaSystem.SetEntityName(printed, val);

                _paperSystem.SetContent((printed, paper), Loc.GetString(
                        "cargo-console-paper-print-text",
                        ("orderNumber", order.OrderId),
                        ("itemName", itemName), // Exodus: describe the goods rather than their packing crate.
                        ("orderQuantity", quantity), // Exodus: quantity in this delivery.
                        ("requester", order.Requester),
                        ("reason", order.Reason),
                        ("approver", order.Approver ?? string.Empty)));

                // attempt to attach the label to the item
                if (TryComp<PaperLabelComponent>(item, out var label))
                {
                    _slots.TryInsert(item, label.LabelSlot, printed, null);
                }
            }

            return quantity; // Exodus: update the queue only after successful delivery.
        }

        #region Station

        private StationBankAccountComponent? GetBankAccount(EntityUid uid, CargoOrderConsoleComponent _)
        {
            var station = _station.GetOwningStation(uid);

            TryComp<StationBankAccountComponent>(station, out var bankComponent);
            return bankComponent;
        }

        private bool TryGetOrderDatabase(EntityUid uid, [MaybeNullWhen(false)] out EntityUid? dbUid, [MaybeNullWhen(false)] out StationCargoOrderDatabaseComponent dbComp, CargoOrderConsoleComponent _)
        {
            dbUid = _station.GetOwningStation(uid);
            return TryComp(dbUid, out dbComp);
        }

        #endregion
    }
}
