// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Robust.Shared.Map.Components;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private DynamicMarketSystem _dynamicMarket = default!;

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
