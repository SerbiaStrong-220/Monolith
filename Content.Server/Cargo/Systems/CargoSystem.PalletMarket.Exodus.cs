// (c) Space Exodus Team - EXDS-RL with CLA
// Exodus: market-aware pallet appraisal and fixed bounty rewards.
using Content.Server._Exodus.Cargo;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Cargo.Components;
using Content.Server._NF.Trade;
using Content.Server.Cargo.Components;
using Content.Shared._Exodus.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Trade;
using Content.Shared.Cargo.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind.Components;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private MarketBasketSystem _marketBaskets = default!;

    private readonly HashSet<Entity<CargoPalletComponent>> _palletLookup = new();

    private bool CanSellLivingMob(EntityUid uid)
    {
        return HasComp<CargoSellableMobComponent>(uid) && !HasComp<ActorComponent>(uid) &&
               (!TryComp<MindContainerComponent>(uid, out var mind) || !mind.HasMind);
    }

    private readonly record struct PalletMarketEntry(
        EntityUid Entity,
        MarketItemTax Tax,
        double BasePrice,
        double ConsoleModifier,
        bool IgnoreConsoleModifier,
        string MarketKey,
        bool FixedReward = false,
        double Units = 1);

    /// <summary>
    /// Collects independent commodity prices. A handled price owns its entire subtree.
    /// Completed bounties can only be sold as roots because the bounty completion handler processes roots.
    /// </summary>
    private bool AddPricedEntities(
        EntityUid uid,
        EntityUid taxEntity,
        EntityUid gridUid,
        double consoleModifier,
        bool ignoreConsoleModifier,
        List<PalletMarketEntry> priced,
        HashSet<(EntityUid Station, string Id)> bounties)
    {
        if (TryGetPalletBounty(uid, out var bountyKey, out var reward))
        {
            if (uid != taxEntity || HasNestedPalletBounty(uid) || !bounties.Add(bountyKey))
                return false;

            priced.Add(new PalletMarketEntry(uid, _marketBaskets.GetEntityTax(uid), reward, 1, true, string.Empty,
                FixedReward: true));
            return true;
        }

        if (HasNestedPalletBounty(uid) ||
            !_marketBaskets.TryGetEntityBasket(uid, out var basket, out _, gridUid))
            return false;

        foreach (var line in basket.Lines)
        {
            if (line.UnitBasePrice <= 0 || line.Quantity <= 0)
                continue;

            var modifier = line.IgnoreMarketModifier ? 1 : consoleModifier;
            // Exodus: pay each commodity's own tax; a container cannot tax unrelated contents.
            priced.Add(new PalletMarketEntry(line.SourceEntity ?? uid, line.Tax, line.UnitBasePrice,
                modifier, ignoreConsoleModifier || line.IgnoreMarketModifier, line.MarketKey, Units: line.Quantity));
        }

        return true;
    }

    private bool TryGetPalletBounty(EntityUid uid, out (EntityUid Station, string Id) key, out int reward)
    {
        key = default;
        reward = 0;
        if (!TryGetBountyLabel(uid, out _, out var label) ||
            label.AssociatedStationId is not { } station ||
            !TryComp<StationCargoBountyDatabaseComponent>(station, out var database) ||
            !TryGetBountyFromId(station, label.Id, out var bounty, database) ||
            !_protoMan.TryIndex(bounty.Value.Bounty, out var prototype) ||
            !IsBountyComplete(uid, prototype))
        {
            return false;
        }

        key = (station, label.Id);
        reward = Math.Max(0, prototype.Reward);
        return true;
    }

    private bool HasNestedPalletBounty(EntityUid uid)
    {
        if (!_containerQuery.TryGetComponent(uid, out var containers))
            return false;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (TryGetPalletBounty(contained, out _, out _) || HasNestedPalletBounty(contained))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Quotes a sale without changing global market factors. Display grouping is optional and only built for UI refreshes.
    /// </summary>
    private void GetPalletGoods(
        Entity<CargoPalletConsoleComponent> console,
        EntityUid gridUid,
        out HashSet<EntityUid> toSell,
        out double amount,
        out double noMultiplierAmount,
        out double blackMarketTaxAmount,
        out double frontierTaxAmount,
        out double nfsdTaxAmount,
        out double medicalTaxAmount,
        out MarketTransactionState marketTx,
        List<CargoPalletAppraisalEntry>? appraisalItems = null,
        HashSet<EntityUid>? marketImpactAppliedRoots = null)
    {
        amount = 0;
        noMultiplierAmount = 0;
        blackMarketTaxAmount = 0;
        frontierTaxAmount = 0;
        nfsdTaxAmount = 0;
        medicalTaxAmount = 0;
        toSell = new HashSet<EntityUid>();
        marketTx = new MarketTransactionState();
        var priced = new List<PalletMarketEntry>();
        var bounties = new HashSet<(EntityUid Station, string Id)>();
        var appraisalIndices = appraisalItems == null ? null : new Dictionary<(EntProtoId? PrototypeId, string Name), int>();
        var appraisalTotals = appraisalItems == null ? null : new List<double>();
        var entityAppraisals = appraisalItems == null ? null : new Dictionary<EntityUid, double>();

        // Pallet overlap must not repeat price events, bounty validation or recursive container scans.
        _setEnts.Clear();
        foreach (var (palletUid, _, _) in GetCargoPallets(console, gridUid, BuySellType.Sell))
        {
            _lookup.GetEntitiesIntersecting(palletUid, _setEnts, LookupFlags.Dynamic | LookupFlags.Sundries);
        }

        var hasModifier = TryComp<MarketModifierComponent>(console, out var marketModifier) && !marketModifier.Buy;
        foreach (var ent in _setEnts)
        {
            if (!_xformQuery.TryGetComponent(ent, out var xform) ||
                xform.Anchored || !CanSell(ent, xform) ||
                _whitelist.IsWhitelistFail(console.Comp.Whitelist, ent))
            {
                continue;
            }

            var station = _station.GetOwningStation(ent);
            var ignoreModifier = HasComp<IgnoreMarketModifierComponent>(ent);
            var isTradeCrate = HasComp<TradeCrateComponent>(ent);
            var hasWildcard = TryComp<TradeCrateWildcardDestinationComponent>(station, out var wildcard);
            var multiplier = 1.0;
            if (!ignoreModifier)
            {
                if (hasWildcard && isTradeCrate)
                    multiplier = wildcard!.ValueMultiplier;
                else if (station != null && !hasWildcard && hasModifier && !isTradeCrate)
                    multiplier = marketModifier!.Mod;
            }

            var start = priced.Count;
            if (!AddPricedEntities(ent, ent, gridUid, multiplier, ignoreModifier, priced, bounties))
            {
                priced.RemoveRange(start, priced.Count - start);
                continue;
            }

            if (priced.Count > start)
            {
                toSell.Add(ent);
                // Only ordinary prices contribute to marketTx. A fixed bounty's goods still need
                // pressure in universal intake, even when they share a commodity with another root.
                if (marketImpactAppliedRoots != null)
                {
                    for (var index = start; index < priced.Count; index++)
                    {
                        if (priced[index].FixedReward)
                            continue;

                        marketImpactAppliedRoots.Add(ent);
                        break;
                    }
                }
            }
        }

        priced.Sort(static (a, b) =>
        {
            var keyComparison = string.CompareOrdinal(a.MarketKey, b.MarketKey);
            if (keyComparison != 0)
                return keyComparison;

            var priceComparison = a.BasePrice.CompareTo(b.BasePrice);
            return priceComparison != 0 ? priceComparison : a.Entity.CompareTo(b.Entity);
        });

        foreach (var entry in priced)
        {
            var adjustedPrice = entry.FixedReward
                ? entry.BasePrice
                : _dynamicMarket.CalculateSequentialSellValue(entry.MarketKey, entry.BasePrice, entry.Units,
                    1, entry.ConsoleModifier, marketTx, applyImpact: false);
            if (entry.IgnoreConsoleModifier)
                noMultiplierAmount += adjustedPrice;
            else
                amount += adjustedPrice;

            if (entityAppraisals != null)
                entityAppraisals[entry.Entity] = entityAppraisals.GetValueOrDefault(entry.Entity) + adjustedPrice;

            blackMarketTaxAmount += adjustedPrice * entry.Tax.BlackMarket;
            frontierTaxAmount += adjustedPrice * entry.Tax.Frontier;
            nfsdTaxAmount += adjustedPrice * entry.Tax.Nfsd;
            medicalTaxAmount += adjustedPrice * entry.Tax.Medical;
        }

        if (appraisalItems == null)
            return;

        foreach (var (entity, price) in entityAppraisals!)
            AddPalletAppraisal(entity, price, appraisalItems, appraisalIndices!, appraisalTotals!);

        double cumulativePrice = 0;
        var previousPrice = 0;
        var totalPrice = DynamicMarketSystem.RoundSellPayout(amount + noMultiplierAmount);
        for (var i = 0; i < appraisalItems.Count; i++)
        {
            cumulativePrice += appraisalTotals![i];
            var nextPrice = i == appraisalItems.Count - 1
                ? totalPrice
                : Math.Min(totalPrice, DynamicMarketSystem.RoundSellPayout(cumulativePrice));
            var entry = appraisalItems[i];
            entry.Price = nextPrice - previousPrice;
            entry.UnitPrice = (double) entry.Price / entry.Quantity;
            previousPrice = nextPrice;
        }
    }

    private void AddPalletAppraisal(
        EntityUid uid,
        double price,
        List<CargoPalletAppraisalEntry> items,
        Dictionary<(EntProtoId? PrototypeId, string Name), int> indices,
        List<double> totals)
    {
        var quantity = _stackQuery.TryGetComponent(uid, out var stack) ? Math.Max(1, stack.Count) : 1;
        var displayName = Identity.Name(uid, EntityManager);
        EntProtoId? prototypeId = Prototype(uid)?.ID;
        var key = (prototypeId, displayName);
        if (indices.TryGetValue(key, out var index))
        {
            var entry = items[index];
            entry.Quantity = (int) Math.Min(int.MaxValue, (long) entry.Quantity + quantity);
            totals[index] += price;
            return;
        }

        indices.Add(key, items.Count);
        totals.Add(price);
        items.Add(new CargoPalletAppraisalEntry
        {
            Name = displayName,
            PrototypeId = prototypeId,
            Quantity = quantity,
        });
    }
}
