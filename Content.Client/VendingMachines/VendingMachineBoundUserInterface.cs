using Content.Client.UserInterface.Controls;
using Content.Client.VendingMachines.UI;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Client.GameObjects;
using Content.Shared._NF.Bank.Components; // Frontier
using Content.Shared._Exodus.Economy; // Exodus
using Robust.Shared.Prototypes; // Exodus

namespace Content.Client.VendingMachines
{
    public sealed class VendingMachineBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private VendingMachineMenu? _menu;

        [ViewVariables]
        private List<VendingMachineInventoryEntry> _cachedInventory = new();

        // Frontier: market price modifier & balance
        private UserInterfaceSystem _uiSystem = default!;
        private Dictionary<EntProtoId, int?> _prices = new(); // Exodus: server quotes replace local price estimates.
        [ViewVariables]
        private int _balance = 0;
        [ViewVariables]
        private int _cashSlotBalance = 0;
        // End Frontier

        public VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            // Exodus: prices arrive in the server UI state.
            _uiSystem = EntMan.System<UserInterfaceSystem>();

            _menu = this.CreateWindowCenteredLeft<VendingMachineMenu>();
            // Frontier: no exceptions
            if (EntMan.TryGetComponent(Owner, out MetaDataComponent? meta))
                _menu.Title = meta.EntityName;
            else
                _menu.Title = Loc.GetString("vending-machine-nf-fallback-title");
            // End Frontier: no exceptions
            _menu.OnItemSelected += OnItemSelected;
            Refresh();
        }

        public void Refresh()
        {
            var system = EntMan.System<VendingMachineSystem>();
            _cachedInventory = system.GetAllInventory(Owner);

            // Frontier: state, market modifier, balance status
            _balance = 0; // Exodus: do not retain the balance of a previous UI actor.
            var uiUsers = _uiSystem.GetActors(Owner, UiKey);
            foreach (var uiUser in uiUsers)
            {
                if (EntMan.TryGetComponent<BankAccountComponent>(uiUser, out var bank))
                    _balance = bank.Balance;
            }
            int? cashSlotValue = null;
            if (EntMan.TryGetComponent<VendingMachineComponent>(Owner, out var vendingMachine))
            {
                _cashSlotBalance = vendingMachine.CashSlotBalance;
                if (vendingMachine.CashSlotName != null)
                    cashSlotValue = _cashSlotBalance;
            }
            else
            {
                _cashSlotBalance = 0;
            }
            // End Frontier

            _menu?.Populate(_cachedInventory, _prices, _balance, cashSlotValue); // Exodus: display authoritative prices.
        }

        // Exodus-begin
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            if (state is not VendingMachinePriceState prices)
                return;

            _prices = prices.Prices;
            if (_menu != null)
                Refresh();
        }
        // Exodus-end

        private void OnItemSelected(GUIBoundKeyEventArgs args, ListData data)
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (data is not VendorItemsListData { ItemIndex: var itemIndex })
                return;

            // Exodus-begin: never submit an unknown or unavailable quote.
            if (itemIndex < 0 || itemIndex >= _cachedInventory.Count)
                return;

            var selectedItem = _cachedInventory[itemIndex];

            if (selectedItem.Amount == 0 || !_prices.TryGetValue(selectedItem.ID, out var price) || price == null)
                return;

            SendMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID, price));
            // Exodus-end
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            if (_menu == null)
                return;

            _menu.OnItemSelected -= OnItemSelected;
            _menu.OnClose -= Close;
            _menu.Dispose();
        }
    }
}
