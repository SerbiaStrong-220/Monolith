using System.Linq;
using Content.Server._Exodus.Biocode;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Store.Systems;
using Content.Shared._Exodus.Biocode;
using Content.Shared._Exodus.Store;
using Content.Shared.Power;
using Content.Shared.Popups;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Store;

public sealed partial class SummoningMachineSystem : EntitySystem
{
    [Dependency] private BiocodeSystem _biocode = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SummoningMachineComponent, BeforeStoreBuyAttemptEvent>(OnBeforeStoreBuyAttempt);
        SubscribeLocalEvent<SummoningMachineComponent, GetStoreUiDataEvent>(OnGetStoreUiData);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var elapsed = TimeSpan.FromSeconds(frameTime);
        var query = EntityQueryEnumerator<SummoningMachineComponent, StoreComponent, ApcPowerReceiverComponent, PowerChargeComponent>();
        while (query.MoveNext(out var uid, out var summoner, out var store, out var receiver, out var charge))
        {
            var ready = CanOperate(receiver, charge);
            UpdateVisual(uid, summoner, ready);

            if (summoner.ActiveListingId == null)
                continue;

            if (ready)
                summoner.RemainingDuration -= elapsed;

            if (summoner.RemainingDuration <= TimeSpan.Zero)
            {
                CompleteSummon(uid, summoner, store);
                continue;
            }

            summoner.UiAccumulator += elapsed;
            if (summoner.UiAccumulator >= summoner.UiUpdateInterval && _ui.IsUiOpen(uid, StoreUiKey.Key))
            {
                summoner.UiAccumulator = TimeSpan.Zero;
                _store.UpdateUserInterface(null, uid, store);
            }
        }
    }

    private void OnBeforeStoreBuyAttempt(Entity<SummoningMachineComponent> ent, ref BeforeStoreBuyAttemptEvent args)
    {
        args.Handled = true;

        if (TryComp<BiocodeComponent>(ent.Owner, out var biocode) &&
            biocode.BlockInteraction &&
            !_biocode.TryAccess((ent.Owner, biocode), args.Buyer))
        {
            return;
        }

        if (ent.Comp.ActiveListingId != null)
        {
            _popup.PopupEntity(Loc.GetString("summoning-machine-popup-busy"), ent.Owner, args.Buyer, PopupType.SmallCaution);
            return;
        }

        if (!TryComp(ent.Owner, out ApcPowerReceiverComponent? receiver) ||
            !TryComp(ent.Owner, out PowerChargeComponent? charge) ||
            !CanOperate(receiver, charge))
        {
            _popup.PopupEntity(Loc.GetString("summoning-machine-popup-unavailable"), ent.Owner, args.Buyer, PopupType.SmallCaution);
            return;
        }

        if (args.Listing.ProductEntity == null)
        {
            _popup.PopupEntity(Loc.GetString("summoning-machine-popup-unsupported"), ent.Owner, args.Buyer, PopupType.SmallCaution);
            return;
        }

        _store.MarkListingPurchased(args.Listing); // Exodus

        var duration = GetSummonDuration(args.Listing, ent.Comp);
        ent.Comp.ActiveListingId = args.Listing.ID;
        ent.Comp.ActiveProductEntity = args.Listing.ProductEntity;
        ent.Comp.ActiveDuration = duration;
        ent.Comp.RemainingDuration = duration;
        ent.Comp.UiAccumulator = ent.Comp.UiUpdateInterval;

        _store.UpdateUserInterface(args.Buyer, args.StoreUid, args.Store);
    }

    private void OnGetStoreUiData(Entity<SummoningMachineComponent> ent, ref GetStoreUiDataEvent args)
    {
        args.Mode = StoreUiMode.Summoning;
        args.SummoningPriceMultiplier = ent.Comp.DurationMultiplier * ent.Comp.SecondsPerCostUnit;

        if (ent.Comp.ActiveListingId == null)
            return;

        var paused = true;
        if (TryComp(ent.Owner, out ApcPowerReceiverComponent? receiver) &&
            TryComp(ent.Owner, out PowerChargeComponent? charge))
        {
            paused = !CanOperate(receiver, charge);
        }

        args.ActiveSummoning = new StoreSummoningUiData(
            ent.Comp.ActiveListingId.Value,
            ent.Comp.ActiveDuration,
            ent.Comp.RemainingDuration,
            paused);
    }

    public void RefreshActiveSummon(Entity<SummoningMachineComponent, StoreComponent> ent)
    {
        var (uid, summoning, store) = ent;

        if (summoning.ActiveListingId == null ||
            summoning.ActiveDuration <= TimeSpan.Zero)
        {
            return;
        }

        ListingDataWithCostModifiers? listing = null;
        foreach (var candidate in store.FullListingsCatalog)
        {
            if (candidate.ID != summoning.ActiveListingId.Value)
                continue;

            listing = candidate;
            break;
        }

        if (listing == null)
            return;

        var newDuration = GetSummonDuration(listing, summoning);
        if (newDuration == summoning.ActiveDuration)
            return;

        var oldDurationTicks = Math.Max(1L, summoning.ActiveDuration.Ticks);
        var remainingTicks = Math.Clamp(summoning.RemainingDuration.Ticks, 0L, oldDurationTicks);
        var remainingRatio = remainingTicks / (double) oldDurationTicks;
        var newRemainingTicks = Math.Clamp((long) Math.Ceiling(newDuration.Ticks * remainingRatio), 0L, newDuration.Ticks);

        summoning.ActiveDuration = newDuration;
        summoning.RemainingDuration = TimeSpan.FromTicks(newRemainingTicks);
        summoning.UiAccumulator = summoning.UiUpdateInterval;

        _store.UpdateUserInterface(null, uid, store);
    }

    private void CompleteSummon(EntityUid uid, SummoningMachineComponent component, StoreComponent store)
    {
        if (component.ActiveProductEntity != null)
        {
            var direction = _random.NextAngle().ToVec();
            var coordinates = Transform(uid).Coordinates.Offset(direction * 3f);
            var product = Spawn(component.ActiveProductEntity.Value, coordinates);
            _throwing.TryThrow(product, direction, component.EjectSpeed, uid);
        }

        ClearSummon(component);
        _store.UpdateUserInterface(null, uid, store);

        var ready = false;
        if (TryComp(uid, out ApcPowerReceiverComponent? receiver) &&
            TryComp(uid, out PowerChargeComponent? charge))
        {
            ready = CanOperate(receiver, charge);
        }

        UpdateVisual(uid, component, ready);
    }

    private void ClearSummon(SummoningMachineComponent component)
    {
        component.ActiveListingId = null;
        component.ActiveProductEntity = null;
        component.ActiveDuration = TimeSpan.Zero;
        component.RemainingDuration = TimeSpan.Zero;
        component.UiAccumulator = TimeSpan.Zero;
    }

    private TimeSpan GetSummonDuration(ListingDataWithCostModifiers listing, SummoningMachineComponent component)
    {
        var totalCost = listing.Cost.Values.Sum(cost => cost.Float());
        var seconds = Math.Max(1f, totalCost * component.SecondsPerCostUnit * component.DurationMultiplier);
        return TimeSpan.FromSeconds(MathF.Ceiling(seconds));
    }

    private static bool CanOperate(ApcPowerReceiverComponent receiver, PowerChargeComponent charge)
    {
        return receiver.Powered && charge.Active;
    }

    private void UpdateVisual(EntityUid uid, SummoningMachineComponent component, bool ready)
    {
        var newState = ready
            ? component.ActiveListingId != null
                ? SummoningMachineVisualState.Working
                : SummoningMachineVisualState.Idle
            : SummoningMachineVisualState.Inactive;

        if (component.VisualState == newState)
            return;

        component.VisualState = newState;
        _appearance.SetData(uid, SummoningMachineVisuals.State, newState);
    }
}
