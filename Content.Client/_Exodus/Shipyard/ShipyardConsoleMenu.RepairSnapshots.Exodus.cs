using Content.Shared._Exodus.Shipyard;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.BUI;

namespace Content.Client._NF.Shipyard.UI;

public sealed partial class ShipyardConsoleMenu
{
    public event Action<RepairSnapshotQuote>? OnRepairSnapshot;

    private RepairSnapshotQuote? _repairSnapshotQuote;

    private void InitializeRepairSnapshots()
    {
        RepairSnapshotButton.ResetTime = TimeSpan.FromSeconds(10);
        RepairSnapshotButton.OnPressed += _ =>
        {
            if (_repairSnapshotQuote is not { } quote)
                return;

            RepairSnapshotButton.Disabled = true;
            OnRepairSnapshot?.Invoke(quote);
        };
    }

    private void UpdateRepairSnapshot(ShipyardConsoleInterfaceState state)
    {
        var changed = _repairSnapshotQuote != state.RepairSnapshot;
        _repairSnapshotQuote = state.RepairSnapshot;
        RepairSnapshotContainer.Visible = _repairSnapshotQuote != null;

        var available = state.AccessGranted && state.IsTargetIdPresent &&
                        _repairSnapshotQuote is { } offer && state.Balance >= offer.Price;
        if (changed || !available)
        {
            // ConfirmButton otherwise re-enables itself after its confirmation cooldown.
            RepairSnapshotButton.IsConfirming = false;
            RepairSnapshotButton.Disabled = true;
        }

        if (_repairSnapshotQuote is { } quote)
        {
            var price = BankSystemExtensions.ToSpesoString(quote.Price);
            RepairSnapshotButton.Text = Loc.GetString("shipyard-snapshot-button-price", ("price", price));
            RepairSnapshotButton.ConfirmationText = Loc.GetString("shipyard-snapshot-confirm-price", ("price", price));
        }

        if (!RepairSnapshotButton.IsConfirming)
            RepairSnapshotButton.Disabled = !available;
    }
}
