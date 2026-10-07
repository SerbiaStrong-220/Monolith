// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Shipyard;
using Content.Shared.Materials;
using Content.Shared.Mind.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketStockIntakeSystem
{
    /// <summary>
    /// Captures quantities at purchase, including installed parts. Moving, replacing or splitting
    /// entities does not change this allowance, and a repair snapshot never calls this method.
    /// </summary>
    public void CaptureOriginalContents(Entity<ShipyardOriginalContentsComponent> ship)
    {
        var roots = new List<EntityUid>();
        var children = Transform(ship).ChildEnumerator;
        while (children.MoveNext(out var child))
            roots.Add(child);

        var contents = CollectShipContents(roots);
        ship.Comp.StockQuantities = contents.StockQuantities;
        ship.Comp.CommodityQuantities.Clear();
        foreach (var line in contents.Commodities)
            AddQuantity(ship.Comp.CommodityQuantities, line.MarketKey, line.Quantity);
    }

    private ShipContents CollectShipContents(IReadOnlyCollection<EntityUid> sold)
    {
        var result = new ShipContents();
        var roots = new HashSet<EntityUid>(sold);
        var visited = new HashSet<EntityUid>();
        var pending = new Stack<(EntityUid Uid, bool ImpactSuppressed)>();
        foreach (var root in roots)
        {
            if (!HasSoldAncestor(root, roots))
                pending.Push((root, false));
        }

        while (pending.TryPop(out var entry))
        {
            var (uid, impactSuppressed) = entry;
            if (!visited.Add(uid) || TerminatingOrDeleted(uid) || HasComp<MapGridComponent>(uid) ||
                HasComp<MarketStockProcessedComponent>(uid) || HasComp<ActorComponent>(uid) ||
                TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                continue;

            result.Entities.Add(uid);
            string? ownKey = null;
            if (TryGetShipItem(uid, out var item))
            {
                ownKey = item.Key;
                result.Items.Add(item);
                AddQuantity(result.StockQuantities, ownKey, item.Count);
            }

            HashSet<string>? storedKeys = null;
            if (TryComp<MaterialStorageComponent>(uid, out var storage))
            {
                storedKeys = new HashSet<string>();
                CollectShipMaterials((uid, storage), result, storedKeys);
            }

            if (_baskets.TryGetEntityOwnBasket(uid, out var basket, out var includesContents, out _) && basket.Exact)
            {
                foreach (var line in basket.Lines)
                {
                    // Virtual ammunition/materials must retain their allowance when extracted later.
                    // Physical entities and stored materials were counted above, even with no sale price.
                    if (line.MarketKey != ownKey && storedKeys?.Contains(line.MarketKey) != true)
                        AddQuantity(result.StockQuantities, line.MarketKey, line.Quantity);
                    if (!impactSuppressed)
                        result.Commodities.Add(line);
                }
                impactSuppressed |= includesContents;
            }

            if (!TryComp<ContainerManagerComponent>(uid, out var containers))
                continue;

            foreach (var container in containers.Containers.Values)
            {
                // Count installed parts too: they remain original equipment if removed from the machine.
                foreach (var child in container.ContainedEntities)
                    pending.Push((child, impactSuppressed));
            }
        }

        return result;
    }

    private bool TryGetShipItem(EntityUid uid, out ShipStockItem item)
    {
        item = default;
        if (!TryPrototype(uid, out var prototype))
            return false;

        var count = 1;
        string? stackId = null;
        if (TryComp<StackComponent>(uid, out var stack))
        {
            count = stack.Count;
            stackId = stack.StackTypeId;
            if (!_prototypes.TryIndex<StackPrototype>(stackId, out var stackType) ||
                !_prototypes.TryIndex(stackType.Spawn, out prototype))
                return false;
        }

        if (count <= 0)
            return false;

        var key = stackId == null
            ? DynamicMarketSystem.ProtoKey(prototype.ID)
            : DynamicMarketSystem.StackKey(stackId);
        item = new ShipStockItem(uid, prototype.ID, key, count, stackId);
        return true;
    }

    private void CollectShipMaterials(Entity<MaterialStorageComponent> storage, ShipContents contents,
        HashSet<string> storedKeys)
    {
        foreach (var (materialId, amount) in storage.Comp.Storage)
        {
            if (amount <= 0 || !_prototypes.TryIndex(materialId, out var material) ||
                material.StackEntity is not { } stackEntity || !_prototypes.TryIndex(stackEntity, out var prototype) ||
                !prototype.TryGetComponent<PhysicalCompositionComponent>(out var composition, Factory) ||
                !prototype.TryGetComponent<StackComponent>(out var stack, Factory) ||
                !composition.MaterialComposition.TryGetValue(materialId, out var perUnit) || perUnit <= 0)
                continue;

            var key = DynamicMarketSystem.StackKey(stack.StackTypeId);
            storedKeys.Add(key);
            AddQuantity(contents.StockQuantities, key, amount / (double) perUnit);
            // Fractional residues affect the baseline, but cannot create a whole unit in stock.
            if (amount / perUnit > 0)
                contents.Items.Add(new ShipStockItem(storage, prototype.ID, key, amount / perUnit,
                    stack.StackTypeId, materialId, perUnit, material.Price * perUnit));
        }
    }

    private static void AddQuantity(Dictionary<string, double> quantities, string key, double amount)
    {
        quantities[key] = quantities.GetValueOrDefault(key) + amount;
    }

    private sealed class ShipContents
    {
        public readonly List<EntityUid> Entities = new();
        public readonly List<ShipStockItem> Items = new();
        public readonly List<MarketBasketLine> Commodities = new();
        public readonly Dictionary<string, double> StockQuantities = new();
    }

    private readonly record struct ShipStockItem(EntityUid Uid, EntProtoId Prototype, string Key, int Count,
        string? StackId, ProtoId<MaterialPrototype>? Material = null, int MaterialPerUnit = 0, double MaterialPrice = 0);
}
