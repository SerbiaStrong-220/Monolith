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

public sealed partial class TerritoryStoreDiscountSystem : EntitySystem
{
    private const string TerritoryDiscountModifierId = "ExodusTerritoryDiscount";

    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SummoningMachineSystem _summoning = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private TerritoryCounterSystem _territoryCounter = default!;

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
        var effectiveScore = GetEffectiveScore(ent.Comp.Faction);
        args.HasPriceModifier = true;
        args.PriceMultiplier = GetPriceEffectFraction(effectiveScore, ent.Comp.DiscountPerPoint);
    }

    private void OnTerritoryScoreChanged(ref TerritoryScoreChangedEvent ev)
    {
        var query = EntityQueryEnumerator<StoreComponent, TerritoryStoreDiscountComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
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

        var effectiveScore = GetEffectiveScore(territoryDiscount.Faction);
        var priceMultiplier = GetPriceMultiplier(effectiveScore, territoryDiscount.DiscountPerPoint);

        foreach (var listing in store.FullListingsCatalog)
        {
            listing.RemoveCostModifier(TerritoryDiscountModifierId);

            if (Math.Abs(priceMultiplier - 1f) <= 0.0001f)
                continue;

            var modifier = BuildModifier(listing, priceMultiplier);
            if (modifier.Count == 0)
                continue;

            listing.AddCostModifier(TerritoryDiscountModifierId, modifier);
        }

        _store.UpdateUserInterface(null, uid, store);

        if (TryComp<SummoningMachineComponent>(uid, out var summoning))
            _summoning.RefreshActiveSummon((uid, summoning, store));
    }

    private static Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> BuildModifier(ListingDataWithCostModifiers listing, float priceMultiplier)
    {
        var modifier = new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>();

        foreach (var (currency, amount) in listing.Cost)
        {
            if (amount <= FixedPoint2.Zero)
                continue;

            var roundedAmount = Math.Ceiling(amount.Float() * priceMultiplier);
            if (roundedAmount < 1d)
                roundedAmount = 1d;

            var modifiedFixedPoint = FixedPoint2.New(roundedAmount);
            var delta = modifiedFixedPoint - amount;
            if (delta == FixedPoint2.Zero)
                continue;

            modifier[currency] = delta;
        }

        return modifier;
    }

    private int GetEffectiveScore(ProtoId<TerritoryFactionPrototype> faction)
    {
        var ownScore = _territoryCounter.GetScore(faction);
        var negativeInfluence = 0;
        var scores = _territoryCounter.GetAllScores();

        foreach (var (otherFaction, score) in scores)
        {
            if (otherFaction == faction ||
                score == 0 ||
                !_proto.TryIndex<TerritoryFactionPrototype>(otherFaction, out var prototype) ||
                prototype.DiscountAlignment != TerritoryDiscountAlignment.Negative)
            {
                continue;
            }

            negativeInfluence += score;
        }

        return ownScore - negativeInfluence;
    }

    private static float GetPriceEffectFraction(int effectiveScore, float discountPerPoint)
    {
        return 1f - GetPriceMultiplier(effectiveScore, discountPerPoint);
    }

    private static float GetPriceMultiplier(int effectiveScore, float discountPerPoint)
    {
        if (discountPerPoint <= 0f ||
            discountPerPoint >= 1f)
        {
            return 1f;
        }

        var scoreScale = discountPerPoint / (1f - discountPerPoint);
        if (effectiveScore >= 0)
            return 1f / (1f + effectiveScore * scoreScale);

        return 1f + (-effectiveScore * scoreScale);
    }
}
