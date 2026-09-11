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
using Robust.Shared.Timing;

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
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SummoningMachineComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SummoningMachineComponent, BeforeStoreBuyAttemptEvent>(OnBeforeStoreBuyAttempt);
        SubscribeLocalEvent<SummoningMachineComponent, GetStoreUiDataEvent>(OnGetStoreUiData);
    }

    private void OnStartup(Entity<SummoningMachineComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextUiUpdate = _timing.CurTime + ent.Comp.UiUpdateInterval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var elapsed = TimeSpan.FromSeconds(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SummoningMachineComponent, StoreComponent, ApcPowerReceiverComponent, PowerChargeComponent>();
        while (query.MoveNext(out var uid, out var summoner, out var store, out var receiver, out var charge))
        {
            var ready = CanOperate(receiver, charge);
            UpdateVisual((uid, summoner), ready);

            // Each powered second pays for either the current summon or a future one, never both.
            if (ready)
            {
                var idleTime = elapsed;
                if (summoner.ActiveListingId != null)
                {
                    var spent = TimeSpan.FromTicks(Math.Clamp(summoner.RemainingDuration.Ticks, 0L, elapsed.Ticks));
                    summoner.RemainingDuration -= spent;
                    idleTime -= spent;
                }

                summoner.StoredTime += idleTime;
            }

            if (ready && summoner.ActiveListingId != null && summoner.RemainingDuration <= TimeSpan.Zero)
            {
                CompleteSummon((uid, summoner, store));
                continue;
            }

            if (!_ui.IsUiOpen(uid, StoreUiKey.Key))
            {
                summoner.NextUiUpdate = now + summoner.UiUpdateInterval;
                continue;
            }

            if (now < summoner.NextUiUpdate)
                continue;

            summoner.NextUiUpdate += summoner.UiUpdateInterval;
            // Timers update independently of the full catalog, including while the gateway is idle.
            _ui.ServerSendUiMessage(uid, StoreUiKey.Key,
                new SummoningMachineUpdateMessage(summoner.StoredTime, GetActiveSummoning((uid, summoner), ready)));
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
        var storedTimeUsed = TimeSpan.FromTicks(Math.Min(duration.Ticks, ent.Comp.StoredTime.Ticks));
        ent.Comp.StoredTime -= storedTimeUsed;
        ent.Comp.ActiveListingId = args.Listing.ID;
        ent.Comp.ActiveProductEntity = args.Listing.ProductEntity;
        ent.Comp.ActiveDuration = duration;
        ent.Comp.RemainingDuration = duration - storedTimeUsed;
        ent.Comp.NextUiUpdate = _timing.CurTime;

        if (ent.Comp.RemainingDuration <= TimeSpan.Zero)
        {
            CompleteSummon((ent.Owner, ent.Comp, args.Store));
            return;
        }

        UpdateVisual(ent, true);
        _store.UpdateUserInterface(args.Buyer, args.StoreUid, args.Store);
    }

    private void OnGetStoreUiData(Entity<SummoningMachineComponent> ent, ref GetStoreUiDataEvent args)
    {
        args.Mode = StoreUiMode.Summoning;
        args.SummoningPriceMultiplier = ent.Comp.DurationMultiplier * ent.Comp.SecondsPerCostUnit;
        args.StoredSummoningTime = ent.Comp.StoredTime;

        var ready = TryComp(ent.Owner, out ApcPowerReceiverComponent? receiver) &&
                    TryComp(ent.Owner, out PowerChargeComponent? charge) && CanOperate(receiver, charge);
        args.ActiveSummoning = GetActiveSummoning(ent, ready);
    }

    private static StoreSummoningUiData? GetActiveSummoning(Entity<SummoningMachineComponent> ent, bool ready)
    {
        if (ent.Comp.ActiveListingId == null)
            return null;

        return new StoreSummoningUiData(
            ent.Comp.ActiveListingId.Value,
            ent.Comp.ActiveDuration,
            ent.Comp.RemainingDuration,
            !ready);
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
        summoning.NextUiUpdate = _timing.CurTime;

        _store.UpdateUserInterface(null, uid, store);
    }

    private void CompleteSummon(Entity<SummoningMachineComponent, StoreComponent> ent)
    {
        var (uid, component, store) = ent;
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

        UpdateVisual((uid, component), ready);
    }

    private void ClearSummon(SummoningMachineComponent component)
    {
        component.ActiveListingId = null;
        component.ActiveProductEntity = null;
        component.ActiveDuration = TimeSpan.Zero;
        component.RemainingDuration = TimeSpan.Zero;
        component.NextUiUpdate = _timing.CurTime + component.UiUpdateInterval;
    }

    private TimeSpan GetSummonDuration(ListingDataWithCostModifiers listing, SummoningMachineComponent component)
    {
        var totalCost = listing.Cost.Values.Sum(cost => cost.Float());
        var seconds = Math.Max(1f, totalCost * component.SecondsPerCostUnit * component.DurationMultiplier);
        return TimeSpan.FromSeconds(MathF.Ceiling(seconds));
    }

    private static bool CanOperate(ApcPowerReceiverComponent receiver, PowerChargeComponent charge)
    {
        return receiver.Powered && charge.Active && charge.SwitchedOn && charge.Intact;
    }

    private void UpdateVisual(Entity<SummoningMachineComponent> ent, bool ready)
    {
        var (uid, component) = ent;
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
