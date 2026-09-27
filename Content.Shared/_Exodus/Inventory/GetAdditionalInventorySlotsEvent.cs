using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Inventory;

/// <summary>Independent sources may contribute slots without replacing the body's inventory template.</summary>
[ByRefEvent]
public record struct GetAdditionalInventorySlotsEvent
{
    public List<ProtoId<InventoryTemplatePrototype>>? Templates;
}
