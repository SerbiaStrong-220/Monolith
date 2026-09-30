// Exodus: hide equipment controls while the current form cannot use an inventory.
using Content.Client._Exodus.Genetics;
using Content.Client.UserInterface.Systems.Inventory.Widgets;

namespace Content.Client.UserInterface.Systems.Inventory;

public sealed partial class InventoryUIController
{
    private bool InGeneticForm => GeneticFormPresentationSystem.IsAlternateForm(_entities, _playerUid);

    public void RefreshGeneticFormVisibility()
    {
        var hidden = InGeneticForm;
        if (UIManager.GetActiveUIWidgetOrNull<InventoryGui>() is { } inventory)
            inventory.Visible = !hidden;
        if (!hidden)
            return;
        if (_inventoryHotbar != null)
            _inventoryHotbar.Visible = false;
        _strippingWindow?.Close();
    }
}
