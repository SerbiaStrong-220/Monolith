using Content.Server.Store.Systems;
using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.Store.Components;
using Content.Shared._Exodus.Territory;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Server._Exodus.Store;

public sealed class TerritoryStoreDiscountSystem : EntitySystem
{
    private const string TerritoryDiscountModifierId = "ExodusTerritoryDiscount";

    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly TerritoryCounterSystem _territoryCounter = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerritoryStoreDiscountComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TerritoryStoreDiscountComponent, StoreAddedEvent>(OnStoreAdded);
        SubscribeLocalEvent<TerritoryStoreDiscountComponent, GetStoreUiDataEvent>(OnGetStoreUiData);
        SubscribeLocalEvent<StoreBuyFinishedEvent>(OnStoreBuyFinished);
        SubscribeLocalEvent<TerritoryScoreChangedEvent>(OnTerritoryScoreChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
    }

    private void OnStartup(Entity<TerritoryStoreDiscountComponent> ent, ref ComponentStartup args)
    {
        RefreshStore(ent.Owner);
    }

    private void OnStoreAdded(Entity<TerritoryStoreDiscountComponent> ent, ref StoreAddedEvent args)
    {
        RefreshStore(ent.Owner);
    }

    private void OnStoreBuyFinished(ref StoreBuyFinishedEvent args)
    {
        RefreshStore(args.StoreUid);
    }

    private void OnGetStoreUiData(Entity<TerritoryStoreDiscountComponent> ent, ref GetStoreUiDataEvent args)
    {
        var score = _territoryCounter.GetScore(ent.Comp.Faction);
        args.PriceMultiplier = GetDiscountFraction(score, ent.Comp.DiscountPerPoint);
    }

    private void OnTerritoryScoreChanged(ref TerritoryScoreChangedEvent ev)
    {
        var query = EntityQueryEnumerator<StoreComponent, TerritoryStoreDiscountComponent>();
        while (query.MoveNext(out var uid, out _, out var territoryDiscount))
        {
            if (territoryDiscount.Faction != ev.Faction)
                continue;

            RefreshStore(uid);
        }
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        var query = EntityQueryEnumerator<StoreComponent, TerritoryStoreDiscountComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            RefreshStore(uid);
        }
    }

    private void RefreshStore(EntityUid uid)
    {
        if (!TryComp(uid, out StoreComponent? store) ||
            !TryComp(uid, out TerritoryStoreDiscountComponent? territoryDiscount))
        {
            return;
        }

        var score = _territoryCounter.GetScore(territoryDiscount.Faction);
        var priceMultiplier = GetPriceMultiplier(score, territoryDiscount.DiscountPerPoint);

        foreach (var listing in store.FullListingsCatalog)
        {
            listing.RemoveCostModifier(TerritoryDiscountModifierId);

            if (priceMultiplier >= 0.9999f)
                continue;

            var modifier = BuildModifier(listing, priceMultiplier);
            if (modifier.Count == 0)
                continue;

            listing.AddCostModifier(TerritoryDiscountModifierId, modifier);
        }

        _store.UpdateUserInterface(null, uid, store);
    }

    private static Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> BuildModifier(ListingDataWithCostModifiers listing, float priceMultiplier)
    {
        var modifier = new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>();

        foreach (var (currency, amount) in listing.Cost)
        {
            if (amount <= FixedPoint2.Zero)
                continue;

            var discountedAmount = Math.Ceiling(amount.Float() * priceMultiplier);
            if (discountedAmount < 1d)
                discountedAmount = 1d;

            var discountedFixedPoint = FixedPoint2.New(discountedAmount);
            var delta = discountedFixedPoint - amount;
            if (delta == FixedPoint2.Zero)
                continue;

            modifier[currency] = delta;
        }

        return modifier;
    }

    private static float GetDiscountFraction(int score, float discountPerPoint)
    {
        return 1f - GetPriceMultiplier(score, discountPerPoint);
    }

    private static float GetPriceMultiplier(int score, float discountPerPoint)
    {
        if (score <= 0 ||
            discountPerPoint <= 0f ||
            discountPerPoint >= 1f)
        {
            return 1f;
        }

        var scoreScale = discountPerPoint / (1f - discountPerPoint);
        return 1f / (1f + score * scoreScale);
    }
}
