using Content.Shared._Exodus.Territory;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Territory;

/// <summary>Reconciles priority claims on lifecycle events, never by scanning the sector each tick.</summary>
public sealed partial class TerritoryOverrideSystem : EntitySystem
{
    [Dependency] private GridTerritorySystem _territory = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerritoryOverrideComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TerritoryOverrideComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TerritoryOverrideComponent, AnchorStateChangedEvent>(OnAnchored);
        SubscribeLocalEvent<TerritoryOverrideComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<TerritoryOverrideComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<TerritoryOverrideComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TerritoryOverrideStateComponent, GridTerritoryControlAttemptEvent>(OnControlAttempt);
        SubscribeLocalEvent<GridTerritoryComponent, GridTerritoryInitializedEvent>(OnTerritoryInit);
    }

    public void SetEnabled(Entity<TerritoryOverrideComponent?> ent, bool enabled)
    {
        if (!Resolve(ent, ref ent.Comp, false) || ent.Comp.Enabled == enabled)
            return;

        ent.Comp.Enabled = enabled;
        RefreshClaim((ent.Owner, ent.Comp));
    }

    private void OnStartup(Entity<TerritoryOverrideComponent> ent, ref ComponentStartup args)
    {
        if (MetaData(ent).EntityLifeStage >= EntityLifeStage.MapInitialized)
            RefreshClaim(ent);
    }

    private void OnMapInit(Entity<TerritoryOverrideComponent> ent, ref MapInitEvent args) => RefreshClaim(ent);

    private void OnAnchored(Entity<TerritoryOverrideComponent> ent, ref AnchorStateChangedEvent args) => RefreshClaim(ent);

    private void OnParentChanged(Entity<TerritoryOverrideComponent> ent, ref EntParentChangedMessage args) => RefreshClaim(ent);

    private void OnMobStateChanged(Entity<TerritoryOverrideComponent> ent, ref MobStateChangedEvent args) => RefreshClaim(ent);

    private void OnShutdown(Entity<TerritoryOverrideComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Enabled = false;
        RefreshClaim(ent);
    }

    private void OnTerritoryInit(Entity<GridTerritoryComponent> ent, ref GridTerritoryInitializedEvent args)
    {
        if (!ent.Comp.Claimable || !TryComp<MapGridComponent>(ent, out var grid))
            return;

        // POI loading can add territory after the anchored sources have already initialized.
        foreach (var uid in _maps.GetLocalAnchoredEntities(ent, grid, grid.LocalAABB))
        {
            if (TryComp<TerritoryOverrideComponent>(uid, out var source))
                RefreshClaim((uid, source));
        }

        if (TryComp<TerritoryOverrideStateComponent>(ent, out var state))
            Reconcile((ent.Owner, state));
    }

    private void OnControlAttempt(Entity<TerritoryOverrideStateComponent> ent, ref GridTerritoryControlAttemptEvent args)
    {
        if (ent.Comp.ActiveSource is not { } source ||
            !TryComp<TerritoryOverrideComponent>(source, out var claim) ||
            !TryGetClaimGrid((source, claim), out var grid) || grid != ent.Owner)
            return;

        if (args.Source != source || args.Faction != claim.Faction)
            args.Cancelled = true;
    }

    private bool TryGetClaimGrid(Entity<TerritoryOverrideComponent> ent, out EntityUid grid)
    {
        grid = default;
        if (!ent.Comp.Enabled || !ent.Comp.Initialized || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
            !MetaData(ent).EntityInitialized ||
            ent.Comp.RequiresAlive && (!TryComp<MobStateComponent>(ent, out var mob) || mob.CurrentState != MobState.Alive) ||
            !TryComp<TransformComponent>(ent, out var xform) || !xform.Anchored || xform.GridUid is not { } gridUid ||
            TerminatingOrDeleted(gridUid) || EntityManager.IsQueuedForDeletion(gridUid) ||
            !TryComp<GridTerritoryComponent>(gridUid, out var territory) || !territory.Claimable ||
            territory.LifeStage >= ComponentLifeStage.Stopping)
            return false;

        grid = gridUid;
        return true;
    }

    private void RefreshClaim(Entity<TerritoryOverrideComponent> ent)
    {
        EntityUid? newGrid = TryGetClaimGrid(ent, out var grid) ? grid : null;
        var previousGrid = ent.Comp.RegisteredGrid;
        ent.Comp.RegisteredGrid = newGrid;

        if (previousGrid is { } oldGrid && oldGrid != newGrid &&
            !TerminatingOrDeleted(oldGrid) && TryComp<TerritoryOverrideStateComponent>(oldGrid, out var oldState))
        {
            oldState.Sources.Remove(ent.Owner);
            Reconcile((oldGrid, oldState));
        }

        if (newGrid is not { } target)
            return;

        var state = EnsureComp<TerritoryOverrideStateComponent>(target);
        state.Sources.Add(ent.Owner);
        Reconcile((target, state));
    }

    private void Reconcile(Entity<TerritoryOverrideStateComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
            !TryComp<GridTerritoryComponent>(ent, out var territory) || territory.LifeStage >= ComponentLifeStage.Stopping)
            return;

        Entity<TerritoryOverrideComponent>? selected = null;
        if (ent.Comp.ActiveSource is { } active && TryComp<TerritoryOverrideComponent>(active, out var activeClaim) &&
            TryGetClaimGrid((active, activeClaim), out var activeGrid) && activeGrid == ent.Owner)
            selected = (active, activeClaim);

        foreach (var source in ent.Comp.Sources)
        {
            if (!TryComp<TerritoryOverrideComponent>(source, out var claim) ||
                !TryGetClaimGrid((source, claim), out var grid) || grid != ent.Owner ||
                selected is { } current && current.Comp.Priority >= claim.Priority)
                continue;

            selected = (source, claim);
        }

        if (selected is { } winner)
        {
            if (!ent.Comp.HasPreviousClaim)
            {
                ent.Comp.PreviousFaction = territory.ControllingFaction;
                ent.Comp.PreviousBanner = territory.ActiveClaimBanner;
                ent.Comp.HasPreviousClaim = true;
            }

            ent.Comp.ActiveSource = winner.Owner;
            if (territory.ControllingFaction != winner.Comp.Faction || territory.ActiveClaimBanner != winner.Owner)
                _territory.SetController(ent, winner.Comp.Faction, winner.Owner);
            return;
        }

        ent.Comp.ActiveSource = null;
        if (!ent.Comp.HasPreviousClaim)
            return;

        var previousFaction = ent.Comp.PreviousFaction;
        var previousBanner = ent.Comp.PreviousBanner;
        ent.Comp.HasPreviousClaim = false;
        ent.Comp.PreviousFaction = null;
        ent.Comp.PreviousBanner = null;
        if (previousBanner is not { } banner || TerminatingOrDeleted(banner) || EntityManager.IsQueuedForDeletion(banner) ||
            !TryComp<TerritoryBannerComponent>(banner, out var bannerComp) || bannerComp.LifeStage >= ComponentLifeStage.Stopping ||
            !TryComp<TransformComponent>(banner, out var xform) || !xform.Anchored || xform.GridUid != ent.Owner ||
            previousFaction is { } faction && faction != bannerComp.Faction)
        {
            _territory.ClearController(ent);
            return;
        }

        if (previousFaction != null)
        {
            _territory.SetController(ent, previousFaction, banner);
            return;
        }

        // Interrupted captures must still complete a countdown; they never become instant claims.
        _territory.ClearController(ent);
        _territory.TryStartCapture((ent.Owner, territory), (banner, bannerComp), null);
    }
}
