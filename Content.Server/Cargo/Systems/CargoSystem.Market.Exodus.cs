// (c) Space Exodus Team - EXDS-RL with CLA
// Exodus: dynamic catalog and resale integration.
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Components;
using Content.Shared._Exodus.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Database;
using Content.Shared.Stacks;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private MarketPurchaseSystem _marketPurchases = default!;
    [Dependency] private MarketSellCeilingSystem _marketSellCeilings = default!;
    [Dependency] private MarketInventorySystem _marketInventory = default!;

    // Exodus: identical terminals share one catalog calculation within this notification only.
    private readonly record struct CargoMarketCatalogSnapshot(
        HashSet<string> AllowedGroups,
        double Modifier,
        double PurchaseReturnRate,
        List<CargoMarketListing> Listings);

    // Exodus: shared inventory changes refresh only terminals that currently have viewers.
    private void OnMarketInventoryChanged(ref MarketInventoryChangedEvent args)
    {
        List<CargoMarketCatalogSnapshot>? catalogs = null;
        MarketSellCeiling? ceiling = null;
        var query = EntityQueryEnumerator<CargoOrderConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!_uiSystem.IsUiOpen(uid, CargoConsoleUiKey.Orders))
                continue;

            var station = _station.GetOwningStation(uid);
            var modifier = GetCargoMarketModifier(uid);
            var returnRate = GetCargoPurchaseReturnRate(console);
            List<CargoMarketListing>? listings = null;
            catalogs ??= new List<CargoMarketCatalogSnapshot>();
            foreach (var cached in catalogs)
            {
                if (cached.Modifier.Equals(modifier) && cached.PurchaseReturnRate.Equals(returnRate) &&
                    cached.AllowedGroups.SetEquals(console.AllowedGroups))
                {
                    listings = cached.Listings;
                    break;
                }
            }

            ceiling ??= _marketSellCeilings.GetSnapshot();
            if (listings == null)
            {
                listings = BuildCargoMarketListings((uid, console), ceiling);
                catalogs.Add(new CargoMarketCatalogSnapshot(new HashSet<string>(console.AllowedGroups, StringComparer.Ordinal),
                    modifier, returnRate, listings));
            }

            UpdateOrderState(uid, console, station, listings, ceiling);
        }
    }

    // Exodus-begin: catalog + resale stock listings
    private List<CargoMarketListing> BuildCargoMarketListings(Entity<CargoOrderConsoleComponent> console,
        MarketSellCeiling? limits = null)
    {
        var listings = new List<CargoMarketListing>();
        var ceiling = limits ?? _marketSellCeilings.GetSnapshot();
        var purchaseReturnRate = GetCargoPurchaseReturnRate(console.Comp);

        var consoleMod = GetCargoMarketModifier(console);

        // Entity prototypes already offered by YAML cargo catalog (skip in resale section).
        var catalogEntityIds = new HashSet<EntProtoId>();

        foreach (var product in _protoMan.EnumeratePrototypes<CargoProductPrototype>())
        {
            if (!console.Comp.AllowedGroups.Contains(product.Group))
                continue;

            catalogEntityIds.Add(product.Product);

            var key = _dynamicMarket.GetMarketKeyFromPrototype(product.Product);
            _dynamicMarket.TryGetQuote(key, out var quote);
            var available = _marketPurchases.TryQuotePrototypeBuy(product.Product, 1, product.Cost,
                consoleMod, out var purchaseQuote, purchaseReturnRate: purchaseReturnRate, ceiling: ceiling);

            listings.Add(new CargoMarketListing
            {
                ProductId = product.ID,
                EntityProtoId = product.Product,
                DisplayName = product.Name,
                Category = product.Category,
                UnitPrice = purchaseQuote?.TotalPrice ?? 0,
                Available = available,
                Trend = quote.Trend,
                ChangePercent = quote.ChangePercent,
                StockQuantity = null,
                IsResale = false,
            });
        }

        // Exodus: all cargo terminals share the server-wide resale inventory.
        foreach (var entry in _marketInventory.GetStock())
        {
            if (entry.Quantity <= 0)
                continue;

            if (catalogEntityIds.Contains(entry.Prototype))
                continue;

            if (!_protoMan.TryIndex<EntityPrototype>(entry.Prototype, out var entProto))
                continue;

            var key = _dynamicMarket.GetMarketKeyFromPrototype(entry.Prototype);
            _dynamicMarket.TryGetQuote(key, out var quote);

            // Base unit from sell-time appraisal snapshot, live sector factor on top.
            var available = _marketPurchases.TryQuotePrototypeBuy(entry.Prototype, 1, entry.Price,
                consoleMod, out var purchaseQuote, entProto.TryGetComponent<StackComponent>(out _, Factory),
                purchaseReturnRate, ceiling);

            listings.Add(new CargoMarketListing
            {
                ProductId = CargoMarketListing.MakeResaleProductId(entry.Prototype),
                EntityProtoId = entry.Prototype,
                DisplayName = entProto.Name,
                Category = CargoMarketListing.ResaleCategoryKey,
                UnitPrice = purchaseQuote?.TotalPrice ?? 0,
                Available = available,
                Trend = quote.Trend,
                ChangePercent = quote.ChangePercent,
                StockQuantity = entry.Quantity,
                IsResale = true,
            });
        }

        return listings;
    }

    private void TryAddResaleOrder(
        Entity<CargoOrderConsoleComponent> console,
        EntityUid player,
        Entity<StationCargoOrderDatabaseComponent> orderDatabase,
        CargoConsoleAddOrderMessage args,
        EntProtoId entityProtoId)
    {
        if (!_protoMan.TryIndex<EntityPrototype>(entityProtoId, out var entProto))
        {
            PlayDenySound(console, console.Comp);
            return;
        }

        if (!_market.TryGetStock(orderDatabase, entityProtoId, out var stock))
        {
            ConsolePopup(player, Loc.GetString("cargo-console-resale-out-of-stock"));
            PlayDenySound(console, console.Comp);
            return;
        }

        var amount = Math.Clamp(args.Amount, 1, stock.Quantity);
        var data = new CargoOrderData(
            GenerateOrderId(orderDatabase.Comp),
            entityProtoId,
            entProto.Name,
            stock.Price,
            amount,
            args.Requester,
            args.Reason,
            GetNetEntity(console),
            fromResaleStock: true);
        if (!TryQuoteCargoOrder(console, data, out var quote))
        {
            ConsolePopup(player, Loc.GetString("market-purchase-unavailable"));
            return;
        }

        data.TotalPrice = quote.TotalPrice;

        if (!TryAddOrder(orderDatabase, data, orderDatabase.Comp))
        {
            PlayDenySound(console, console.Comp);
            return;
        }

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(player):user} added resale order [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.ProductId}]");
    }

    private bool TryQuoteCargoOrder(EntityUid console, CargoOrderData order,
        [NotNullWhen(true)] out MarketPurchaseQuote? quote, MarketSellCeiling? ceiling = null)
    {
        var modifier = GetCargoMarketModifier(console);

        var stackUnits = order.FromResaleStock && _protoMan.TryIndex<EntityPrototype>(order.ProductId, out var prototype) &&
            prototype.TryGetComponent<StackComponent>(out _, Factory);
        var returnRate = TryComp<CargoOrderConsoleComponent>(console, out var component)
            ? GetCargoPurchaseReturnRate(component)
            : 0;
        return _marketPurchases.TryQuotePrototypeBuy(order.ProductId, order.OrderQuantity, order.Price,
            modifier, out quote, stackUnits, returnRate, ceiling);
    }

    private double GetCargoMarketModifier(EntityUid console)
    {
        return TryComp<MarketModifierComponent>(console, out var modifier) && modifier.Buy ? modifier.Mod : 1.0;
    }

    private static double GetCargoPurchaseReturnRate(CargoOrderConsoleComponent console)
    {
        double total = 0;
        foreach (var coefficient in console.TaxAccounts.Values)
        {
            if (float.IsFinite(coefficient) && coefficient > 0)
                total += coefficient;
        }

        return total;
    }
    // Exodus-end
}
