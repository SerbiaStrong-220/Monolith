// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Shipyard;
using Content.Server._NF.Market.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketStockIntakeSystem
{
    private void ReceiveShipSale(IReadOnlyCollection<EntityUid> sold, EntityUid source,
        ShipyardOriginalContentsComponent baseline)
    {
        if (sold.Count == 0 || !_market.Ready)
            return;

        // Snapshot before intake consumes materials, and subtract the allowance once for the whole ship.
        var contents = CollectShipContents(sold);
        foreach (var uid in contents.Entities)
            AddComp<MarketStockProcessedComponent>(uid);

        var extra = contents.StockQuantities;
        foreach (var (key, original) in baseline.StockQuantities)
        {
            if (extra.TryGetValue(key, out var current))
                extra[key] = Math.Max(0, current - original);
        }

        var station = _stations.GetOwningStation(source);
        var policy = TryComp<CargoMarketDataComponent>(station, out var local)
            ? local
            : _inventory.GetDefaultAdmissionRules();
        var candidates = new Dictionary<string, List<ShipStockItem>>();
        var compositions = new Dictionary<string, IReadOnlyDictionary<string, double>>();
        foreach (var item in contents.Items)
        {
            if (extra.GetValueOrDefault(item.Key) < 1 || item.Material == null && !CanStockEntity(policy, item.Uid))
                continue;

            if (!candidates.TryGetValue(item.Key, out var items))
            {
                // Recreating uncertain/random contents cannot safely preserve exact purchased allowances.
                if (!TryGetRegeneratedContents(item, out var generated))
                    continue;

                items = new List<ShipStockItem>();
                candidates.Add(item.Key, items);
                compositions.Add(item.Key, generated);
            }
            items.Add(item);
        }

        if (MarketShipyardStockPlan.TryOrder(compositions, out var order))
        {
            foreach (var key in order)
            {
                foreach (var item in candidates[key])
                {
                    var amount = Math.Min(item.Count, (int) Math.Min(int.MaxValue, Math.Floor(extra.GetValueOrDefault(key))));
                    // Every default part recreated by the stock item must also be actual surplus.
                    // Otherwise returning an empty/new shell could reintroduce excluded factory parts.
                    foreach (var (child, quantity) in compositions[key])
                    {
                        if (quantity > 0)
                            amount = (int) Math.Min(amount, Math.Floor(extra.GetValueOrDefault(child) / quantity));
                    }
                    if (amount <= 0 || !TryReceiveShipItem(item, amount))
                        continue;

                    extra[key] -= amount;
                    foreach (var (child, quantity) in compositions[key])
                        extra[child] = Math.Max(0, extra.GetValueOrDefault(child) - quantity * amount);
                }
            }
        }

        // Pressure follows actual added commodities, independently of how they are packaged in stock.
        var remaining = new Dictionary<string, double>(baseline.CommodityQuantities);
        var transaction = new MarketTransactionState();
        foreach (var line in contents.Commodities)
        {
            var excluded = Math.Min(line.Quantity, remaining.GetValueOrDefault(line.MarketKey));
            remaining[line.MarketKey] = remaining.GetValueOrDefault(line.MarketKey) - excluded;
            if (line.Quantity > excluded)
                _market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity - excluded,
                    1, 1, transaction, applyImpact: false);
        }
        _market.CommitTransaction(transaction);
    }

    private bool TryGetRegeneratedContents(ShipStockItem item, out Dictionary<string, double> generated)
    {
        generated = new Dictionary<string, double>();
        if (!_baskets.TryGetPrototypeBasket(item.Prototype, out var basket, out _) ||
            !basket.Exact || basket.Packages.Count != 0)
            return false;

        var units = 1;
        if (item.StackId != null && _prototypes.Index(item.Prototype).TryGetComponent<StackComponent>(out var stack, Factory))
            units = stack.Count;
        if (units <= 0)
            return false;

        double own = 0;
        foreach (var line in basket.Lines)
        {
            if (line.MarketKey == item.Key)
                own += line.Quantity / units;
            else
                AddQuantity(generated, line.MarketKey, line.Quantity / units);
        }
        return own == 1;
    }

    private bool TryReceiveShipItem(ShipStockItem item, int count)
    {
        if (item.Material is not { } material)
            return _inventory.TryAddStock(item.Prototype, count,
                _pricing.GetPrice(item.Uid, includeContents: false) / item.Count, item.StackId);

        if (!TryComp<MaterialStorageComponent>(item.Uid, out var storage))
            return false;

        var consumed = count * item.MaterialPerUnit;
        if (!_materials.CanChangeMaterialAmount(item.Uid, material, -consumed, storage, localOnly: true) ||
            !_inventory.TryAddStock(item.Prototype, count, item.MaterialPrice, item.StackId))
            return false;

        _materials.TryChangeMaterialAmount(item.Uid, material, -consumed, storage, localOnly: true);
        return true;
    }
}
