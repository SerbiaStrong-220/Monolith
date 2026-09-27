// Exodus: support additional inventory slots supplied by independent effects.
using Content.Shared._Exodus.Inventory;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared.Inventory;

public partial class InventorySystem
{
    [Dependency] private readonly INetManager _inventoryNet = default!;

    /// <summary>Rebuild slots after a provider changes, preserving the base inventory and other providers.</summary>
    public void RefreshSlots(Entity<InventoryComponent?> ent)
    {
        if (TerminatingOrDeleted(ent.Owner) || !Resolve(ent.Owner, ref ent.Comp, false))
            return;
        UpdateInventoryTemplate((ent.Owner, ent.Comp));
    }

    private SlotDefinition[] GetExtendedSlots(Entity<InventoryComponent> ent, InventoryTemplatePrototype template)
    {
        var ev = new GetAdditionalInventorySlotsEvent();
        RaiseLocalEvent(ent.Owner, ref ev);
        if (ev.Templates == null || ev.Templates.Count == 0)
            return template.Slots;

        var slots = new List<SlotDefinition>(template.Slots);
        var names = new HashSet<string>();
        foreach (var slot in template.Slots)
            names.Add(slot.Name);
        foreach (var id in ev.Templates)
        {
            if (!_prototypeManager.TryIndex(id, out var extra))
                continue;
            foreach (var slot in extra.Slots)
            {
                if (names.Add(slot.Name))
                    slots.Add(slot);
            }
        }
        return slots.ToArray();
    }

    private void ApplySlotDefinitions(Entity<InventoryComponent> ent, SlotDefinition[] slots)
    {
        var sameNames = ent.Comp.Slots.Length == slots.Length;
        for (var i = 0; sameNames && i < slots.Length; i++)
            sameNames = ent.Comp.Slots[i].Name == slots[i].Name;
        if (sameNames)
        {
            ent.Comp.Slots = slots;
            return;
        }

        // Unequip while the old definitions still exist, so equipment removal events remain valid.
        // The client receives the authoritative container removal; it must not predict item drops here.
        if (_inventoryNet.IsServer)
        {
            var names = new HashSet<string>();
            foreach (var slot in slots)
                names.Add(slot.Name);
            foreach (var old in ent.Comp.Slots)
            {
                if (!names.Contains(old.Name))
                    TryUnequip(ent.Owner, old.Name, silent: true, force: true, inventory: ent.Comp);
            }
        }

        ent.Comp.Slots = slots;
        ent.Comp.Containers = new ContainerSlot[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            // Keep empty containers for removed extensions so container networking can finish in any order.
            var container = _containerSystem.EnsureContainer<ContainerSlot>(ent.Owner, slots[i].Name);
            container.OccludesLight = false;
            ent.Comp.Containers[i] = container;
        }
    }
}
