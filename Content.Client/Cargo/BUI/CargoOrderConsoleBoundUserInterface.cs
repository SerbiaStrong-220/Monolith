using Content.Shared.Cargo;
using Content.Client.Cargo.UI;
using Content.Shared._Exodus.Economy; // Exodus current market listing
using Content.Shared._NF.Bank; // Exodus current market quote
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.IdentityManagement;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.Client.Cargo.BUI
{
    public sealed class CargoOrderConsoleBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private CargoConsoleMenu? _menu;

        /// <summary>
        /// This is the separate popup window for individual orders.
        /// </summary>
        [ViewVariables]
        private CargoConsoleOrderMenu? _orderMenu;

        [ViewVariables]
        public string? AccountName { get; private set; }

        [ViewVariables]
        public int BankBalance { get; private set; }

        [ViewVariables]
        public int OrderCapacity { get; private set; }

        [ViewVariables]
        public int OrderCount { get; private set; }

        /// <summary>
        /// Exodus: currently selected catalog product; resale uses the listing id below.
        /// </summary>
        [ViewVariables]
        private CargoProductPrototype? _product;

        /// <summary>Exodus: listing id sent to server (cargo product id or resale:EntityId).</summary>
        private string? _selectedListingId;

        /// <summary>Exodus: max amount for resale stock orders.</summary>
        private int? _selectedStockCap;

        private bool _selectedAvailable = true; // Exodus: an unquoted listing cannot be ordered.

        public CargoOrderConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            var spriteSystem = EntMan.System<SpriteSystem>();
            var dependencies = IoCManager.Instance!;
            _menu = new CargoConsoleMenu(Owner, EntMan, dependencies.Resolve<IPrototypeManager>(), spriteSystem);
            var localPlayer = dependencies.Resolve<IPlayerManager>().LocalEntity;
            var description = new FormattedMessage();

            string orderRequester;

            if (EntMan.TryGetComponent<MetaDataComponent>(localPlayer, out var metadata))
                orderRequester = Identity.Name(localPlayer.Value, EntMan);
            else
                orderRequester = string.Empty;

            _orderMenu = new CargoConsoleOrderMenu();
            _orderMenu.Amount.IsValid = IsOrderAmountValid; // Exodus: read live stock and capacity.

            _menu.OnClose += Close;

            _menu.OnItemSelected += (args) =>
            {
                if (args.Button.Parent is not CargoProductRow row)
                    return;

                description.Clear();
                description.PushColor(Color.White); // Rich text default color is grey
                description.AddText(row.Description); // Exodus: the card tooltip also includes its name and price.

                _orderMenu.Description.SetMessage(description);
                // Exodus-begin: select catalog or resale stock at the current market price.
                _product = row.Product;
                _selectedListingId = !string.IsNullOrEmpty(row.ListingProductId)
                    ? row.ListingProductId
                    : row.Product?.ID;
                _selectedStockCap = row.StockQuantity;
                _selectedAvailable = !row.MainButton.Disabled; // Exodus
                _orderMenu.ProductName.Text = row.ProductName.Text;
                _orderMenu.PointCost.Text = row.PointCost.Text;
                _orderMenu.Amount.Value = 1;
                UpdateOrderAmount();
                // Exodus-end

                _orderMenu.OpenCentered();
            };
            _menu.OnOrderApproved += ApproveOrder;
            _menu.OnOrderCanceled += RemoveOrder;
            _orderMenu.SubmitButton.OnPressed += (_) =>
            {
                if (AddOrder(orderRequester)) // Exodus: identify the requester automatically without form fields.
                {
                    _orderMenu.Close();
                }
            };

            _menu.OpenCentered();
        }

        private void Populate(List<CargoOrderData> orders)
        {
            if (_menu == null) return;

            // Categories first so product filter uses the preserved selection
            // (balance/order state updates must not reset the player to "All").
            _menu.PopulateCategories(); // Exodus: preserve category across UI refresh
            _menu.PopulateProducts();
            _menu.PopulateOrders(orders);
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not CargoConsoleInterfaceState cState)
                return;

            OrderCapacity = cState.Capacity;
            OrderCount = cState.Count;
            BankBalance = cState.Balance;

            AccountName = cState.Name;

            _menu?.SetMarketListings(cState.MarketListings); // Exodus dynamic market
            Populate(cState.Orders);
            _menu?.UpdateCargoCapacity(OrderCount, OrderCapacity);
            _menu?.UpdateBankData(AccountName, BankBalance);
            UpdateSelectedListing(cState.MarketListings); // Exodus: refresh an already open order dialog.
        }

        // Exodus-begin: keep order entry consistent with live market stock and quotes.
        private bool IsOrderAmountValid(int amount)
        {
            return _selectedAvailable && amount >= 1 && amount <= OrderCapacity &&
                (_selectedStockCap == null || amount <= _selectedStockCap.Value);
        }

        private void UpdateOrderAmount()
        {
            if (_orderMenu == null)
                return;

            // Exodus: keep the full values accessible when the dialog clips long labels.
            _orderMenu.ProductName.ToolTip = _orderMenu.ProductName.Text;
            _orderMenu.PointCost.ToolTip = _orderMenu.PointCost.Text;

            var maxAmount = Math.Min(OrderCapacity, _selectedStockCap ?? OrderCapacity);
            _orderMenu.SubmitButton.Disabled = !_selectedAvailable || maxAmount < 1;
            if (maxAmount >= 1)
                _orderMenu.Amount.Value = Math.Clamp(_orderMenu.Amount.Value, 1, maxAmount);
        }

        private void UpdateSelectedListing(List<CargoMarketListing>? listings)
        {
            if (_selectedListingId == null || _orderMenu == null || _menu == null)
                return;

            if (listings != null)
            {
                foreach (var listing in listings)
                {
                    if (listing.ProductId != _selectedListingId)
                        continue;

                    if (listing.IsResale && listing.StockQuantity != _selectedStockCap)
                    {
                        var description = new FormattedMessage();
                        description.PushColor(Color.White);
                        description.AddText(Loc.GetString("cargo-console-menu-resale-tooltip",
                            ("qty", listing.StockQuantity ?? 0)));
                        _orderMenu.Description.SetMessage(description);
                    }

                    _selectedStockCap = listing.StockQuantity;
                    _selectedAvailable = listing.Available;
                    var name = _menu.ListingDisplayName(listing);
                    _orderMenu.ProductName.Text = listing.IsResale && listing.StockQuantity is { } quantity
                        ? Loc.GetString("cargo-console-menu-resale-name", ("name", name), ("qty", quantity))
                        : name;
                    _orderMenu.PointCost.Text = listing.Available
                        ? BankSystemExtensions.ToSpesoString(listing.UnitPrice)
                        : Loc.GetString("market-purchase-unavailable");
                    UpdateOrderAmount();
                    return;
                }
            }
            else if (_product != null)
            {
                _selectedStockCap = null;
                _selectedAvailable = true;
                _orderMenu.PointCost.Text = BankSystemExtensions.ToSpesoString(_product.Cost);
                UpdateOrderAmount();
                return;
            }

            _selectedListingId = null;
            _selectedStockCap = 0;
            _product = null;
            _orderMenu.SubmitButton.Disabled = true;
            _orderMenu.Close();
        }
        // Exodus-end

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            _menu?.Dispose();
            _orderMenu?.Dispose();
        }

        private bool AddOrder(string requester) // Exodus: requester comes from the local player's identity.
        {
            var orderAmt = _orderMenu?.Amount.Value ?? 0;
            if (!IsOrderAmountValid(orderAmt)) // Exodus: do not silently submit a different quantity.
            {
                return false;
            }

            // Exodus-begin: resale listings use their server-issued id.
            var productId = _selectedListingId ?? _product?.ID ?? "";
            if (string.IsNullOrEmpty(productId))
                return false;
            // Exodus-end

            SendMessage(new CargoConsoleAddOrderMessage(
                requester, // Exodus: preserve order attribution without an editable requester field.
                string.Empty, // Exodus: the simplified form does not request a reason.
                productId,
                orderAmt));

            return true;
        }

        // Exodus-begin: order actions carry their data independently of the card layout.
        private void RemoveOrder(CargoOrderData order)
        {
            SendMessage(new CargoConsoleRemoveOrderMessage(order.OrderId));
        }

        private void ApproveOrder(CargoOrderData order)
        {
            if (OrderCount >= OrderCapacity)
                return;

            SendMessage(new CargoConsoleApproveOrderMessage(order.OrderId, order.TotalPrice));
        }
        // Exodus-end
    }
}
