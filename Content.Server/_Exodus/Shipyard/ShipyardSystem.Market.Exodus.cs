// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.Shipyard;
using Content.Shared.Chemistry.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private DynamicMarketSystem _dynamicMarket = default!;
    [Dependency] private MarketBasketSystem _marketBaskets = default!;

    private const int MaxShipAppraisalDepth = 32;
    private const int MaxShipAppraisalEntities = 65536;

    /// <summary>
    /// Keeps the existing ship appraisal while valuing additional reagents on their shared market.
    /// Purchase contents retain their nominal allowance. This preview never commits market pressure;
    /// the completed sale's stock intake applies surplus quantities exactly once.
    /// </summary>
    public bool TryAppraiseShuttle(EntityUid shuttle, out double price)
    {
        price = 0;
        if (TerminatingOrDeleted(shuttle) || !TryComp<TransformComponent>(shuttle, out var transform))
            return false;

        var remaining = TryComp<ShipyardOriginalContentsComponent>(shuttle, out var baseline)
            ? new Dictionary<string, double>(baseline.CommodityQuantities)
            : new Dictionary<string, double>();
        var transaction = new MarketTransactionState();
        var visited = new HashSet<EntityUid>();
        var pending = new Stack<(EntityUid Uid, int Depth, bool ImpactSuppressed)>();
        var children = transform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (pending.Count >= MaxShipAppraisalEntities)
                return false;
            pending.Push((child, 0, false));
        }

        double correction = 0;
        while (pending.TryPop(out var entry))
        {
            var (uid, depth, impactSuppressed) = entry;
            if (TerminatingOrDeleted(uid) || HasComp<MapGridComponent>(uid) ||
                HasComp<SolutionComponent>(uid) || !LacksPreserveOnSaleComp(uid))
                continue;

            // Validate the whole ship before the legacy nominal appraisal recursively visits containers.
            if (depth > MaxShipAppraisalDepth || !visited.Add(uid) || visited.Count > MaxShipAppraisalEntities)
                return false;

            var suppressContents = impactSuppressed;
            if (!impactSuppressed &&
                _marketBaskets.TryGetEntityOwnBasket(uid, out var basket, out suppressContents, out _))
            {
                foreach (var line in basket.Lines)
                {
                    if (!DynamicMarketSystem.IsReagentKey(line.MarketKey))
                        continue;

                    var excluded = Math.Min(line.Quantity, remaining.GetValueOrDefault(line.MarketKey));
                    remaining[line.MarketKey] = remaining.GetValueOrDefault(line.MarketKey) - excluded;
                    var quantity = line.Quantity - excluded;
                    if (quantity <= 0)
                        continue;

                    correction += _dynamicMarket.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice,
                        quantity, 1, 1, transaction, applyImpact: false) - line.UnitBasePrice * quantity;
                }
            }
            else if (!impactSuppressed)
            {
                // Unprototyped fixtures without priced chemicals remain valid ship equipment.
                // An opaque or unsupported appraisal cannot silently restore a nominal chemical payout.
                // Its custom price can already include chemicals inside preserved descendants.
                if (!_marketBaskets.TryGetReagentContents(uid, out var reagents, out _))
                    return false;

                foreach (var line in reagents.Lines)
                {
                    if (line.UnitBasePrice > 0 && line.Quantity > 0)
                        return false;
                }
            }

            if (!TryComp<ContainerManagerComponent>(uid, out var containers))
                continue;

            foreach (var container in containers.Containers.Values)
            {
                foreach (var child in container.ContainedEntities)
                {
                    if (visited.Count + pending.Count >= MaxShipAppraisalEntities)
                        return false;
                    pending.Push((child, depth + 1, suppressContents));
                }
            }
        }

        var appraisal = _pricing.AppraiseGrid(shuttle, LacksPreserveOnSaleComp) + correction;
        if (!double.IsFinite(appraisal))
            return false;

        price = Math.Max(0, appraisal);
        return true;
    }

    /// <summary>
    /// Sends only the goods still aboard after preserved entities have been moved off the shuttle.
    /// The common intake handles contained goods, price impact and stock filters; grids and tiles are not products.
    /// </summary>
    private void StockSoldShuttleGoods(Entity<TransformComponent> shuttle, EntityUid source)
    {
        var goods = new List<EntityUid>();
        var children = shuttle.Comp.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TerminatingOrDeleted(child) || EntityManager.IsQueuedForDeletion(child) || HasComp<MapGridComponent>(child))
                continue;

            goods.Add(child);
        }

        if (goods.Count == 0)
            return;

        var ev = new MarketGoodsSoldEvent(goods, source, shuttle.Owner);
        RaiseLocalEvent(ref ev);
    }
}
