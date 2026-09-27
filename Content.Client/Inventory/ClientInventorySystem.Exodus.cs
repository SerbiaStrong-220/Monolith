// Exodus: refresh the hotbar when additional inventory slots appear or disappear.
using Content.Shared.Inventory;

namespace Content.Client.Inventory;

public sealed partial class ClientInventorySystem
{
    private void ReconcileSlotControls(Entity<InventoryComponent> ent, InventorySlotsComponent inventorySlots)
    {
        var names = new HashSet<string>();
        foreach (var slot in ent.Comp.Slots)
            names.Add(slot.Name);

        var removed = new List<string>();
        foreach (var (name, data) in inventorySlots.SlotData)
        {
            if (names.Contains(name))
                continue;
            if (ent.Owner == _playerManager.LocalEntity)
                OnSlotRemoved?.Invoke(data);
            removed.Add(name);
        }
        foreach (var name in removed)
            inventorySlots.SlotData.Remove(name);

        foreach (var slot in ent.Comp.Slots)
        {
            TryGetSlotContainer(ent.Owner, slot.Name, out var container, out _);
            if (inventorySlots.SlotData.TryGetValue(slot.Name, out var data))
            {
                data.SlotDef = slot;
                data.Container = container;
                continue;
            }

            data = new SlotData(slot, container);
            inventorySlots.SlotData.Add(slot.Name, data);
            if (ent.Owner == _playerManager.LocalEntity)
                OnSlotAdded?.Invoke(data);
        }
    }
}
