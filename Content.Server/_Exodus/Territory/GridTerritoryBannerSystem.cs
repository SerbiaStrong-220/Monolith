using Content.Server._Mono.Radar;
using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction.Components;
using Content.Shared.Maps;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Handles banners as a claim source for GridTerritory.
/// Enforces "only one active claim banner per grid" at runtime (construction condition handles build time).
/// When a qualifying banner is anchored on a grid with GridTerritoryComponent, it claims control
/// and the radar label updates to the faction name, or the neutral label when removed.
///
/// Factions without final art can use temporary placeholder banner entities.
/// </summary>
public sealed partial class GridTerritoryBannerSystem : EntitySystem
{
    private const float ActiveBannerRadarBlipHalfSize = 1.5f;
    private const float ActiveBannerRadarEdgeVisibilityPadding = 10_000f;

    [Dependency] private GridTerritorySystem _territory = default!;
    [Dependency] private TerritoryClaimRulesSystem _claimRules = default!;
    [Dependency] private TerritoryClaimIntegritySystem _claimIntegrity = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerritoryBannerComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<TerritoryBannerComponent, BeforeAnchoredEvent>(OnBeforeAnchored);
        SubscribeLocalEvent<TerritoryBannerComponent, UserAnchoredEvent>(OnUserAnchored);
        SubscribeLocalEvent<TerritoryBannerComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<TerritoryBannerComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<TerritoryBannerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TerritoryBannerComponent, ComponentShutdown>(OnShutdown);
        // Exodus start - claim mapped banners after GridTerritory is added to an already map-initialized grid (POI spawn).
        SubscribeLocalEvent<GridTerritoryComponent, MapInitEvent>(OnGridTerritoryMapInit);
        // Exodus end
        // ConstructionChangedEvent subscription removed for initial implementation
        // (anchor/parent/shutdown cover unclaim on wrench/deconstruct). Add back with correct event if needed.
        // # Exodus - construction event sub commented for now
    }

    private void OnStartup(Entity<TerritoryBannerComponent> ent, ref ComponentStartup args)
    {
        if (Transform(ent).Anchored)
            TryClaim(ent, false);
    }

    // Exodus start - one-shot scan when territory appears on a loaded POI/station grid.
    private void OnGridTerritoryMapInit(Entity<GridTerritoryComponent> ent, ref MapInitEvent args)
    {
        TryClaimFromAnchoredBannersOnGrid(ent);
    }

    private void TryClaimFromAnchoredBannersOnGrid(Entity<GridTerritoryComponent> territory)
    {
        if (!territory.Comp.Claimable)
            return;

        if (!TryComp<MapGridComponent>(territory.Owner, out var gridComp))
            return;

        foreach (var uid in _map.GetLocalAnchoredEntities(territory.Owner, gridComp, gridComp.LocalAABB))
        {
            if (!TryComp<TerritoryBannerComponent>(uid, out var banner))
                continue;

            TryClaim((uid, banner), false);

            if (territory.Comp.ActiveClaimBanner is { } active && Exists(active))
                return;
        }
    }
    // Exodus end

    private void OnAnchorAttempt(Entity<TerritoryBannerComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var xform = Transform(ent);
        if (!TryResolveBannerGrid(xform, out var grid))
            return;

        if (!TryComp<GridTerritoryComponent>(grid, out var territory))
            return;

        if (!_claimRules.CanStartClaim(ent.Comp.Faction, out var popup))
        {
            _popup.PopupEntity(popup, ent, args.User);
            args.Cancel();
            return;
        }

        if (_claimIntegrity.CanAnchorClaimBanner((grid, territory)))
            return;

        _popup.PopupEntity(Loc.GetString("grid-territory-claim-low-integrity"), ent, args.User);
        args.Cancel();
    }

    private void OnBeforeAnchored(Entity<TerritoryBannerComponent> ent, ref BeforeAnchoredEvent args)
    {
        var pendingActor = EnsureComp<PendingTerritoryClaimActorComponent>(ent.Owner);
        pendingActor.Actor = args.User;
    }

    private void OnAnchorChanged(Entity<TerritoryBannerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            EntityUid? actor = null;
            if (TryComp<PendingTerritoryClaimActorComponent>(ent.Owner, out var pendingActor))
            {
                actor = pendingActor.Actor;
                ClearPendingClaimActor(ent.Owner, pendingActor);
            }

            TryClaim(ent, actor: actor);
        }
        else
        {
            ClearPendingClaimActor(ent.Owner);

            TryUnclaim(ent);
        }
    }

    private void OnUserAnchored(Entity<TerritoryBannerComponent> ent, ref UserAnchoredEvent args)
    {
        ClearPendingClaimActor(ent.Owner);
    }

    private void OnParentChanged(Entity<TerritoryBannerComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldParent is { } oldParent && oldParent != args.Transform.GridUid)
            TryUnclaimFromGrid(ent, oldParent);

        if (args.Transform.Anchored)
            TryClaim(ent);
    }

    private void OnShutdown(Entity<TerritoryBannerComponent> ent, ref ComponentShutdown args)
    {
        ClearActiveBannerBlip(ent.Owner);
        TryUnclaim(ent);
    }

    // OnConstructionChanged removed (event type may differ; anchor/parent/shutdown suffice for now).
    // # Exodus

    private void TryClaim(Entity<TerritoryBannerComponent> banner, bool showPopup = true, EntityUid? actor = null)
    {
        var xform = Transform(banner);
        if (!TryResolveBannerGrid(xform, out var grid))
            return;

        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

        if (!terr.Claimable)
        {
            if (showPopup)
                _popup.PopupEntity(Loc.GetString("grid-territory-claim-disabled"), banner);

            return;
        }

        // Already claimed by this exact banner?
        if (terr.ActiveClaimBanner == banner.Owner)
        {
            ConfigureActiveBannerBlip(banner, (grid, terr));
            return;
        }

        // Check for existing claim.
        if (terr.ActiveClaimBanner is { } existing && existing != banner.Owner)
        {
            if (Exists(existing))
            {
                if (showPopup)
                    _popup.PopupEntity(Loc.GetString("grid-territory-already-claimed"), banner);

                // Do not claim; the new banner is physically there but does not grant control.
                // (Construction condition should have already prevented most cases.)
                return;
            }

            _territory.ClearController(grid);
        }

        // Perform the claim. Label is resolved from the TerritoryFactionPrototype.
        _territory.SetController(grid, banner.Comp.Faction, banner.Owner, actor);
        ConfigureActiveBannerBlip(banner, (grid, terr));

        if (showPopup)
        {
            if (terr.ActiveClaimBanner == banner.Owner &&
                terr.ControllingFaction is { } controllingFaction &&
                controllingFaction.Equals(banner.Comp.Faction))
            {
                _claimRules.RecordSuccessfulClaim(banner.Comp.Faction);
            }

            _popup.PopupEntity(Loc.GetString("grid-territory-claimed"), banner);
        }
    }

    private void TryUnclaim(Entity<TerritoryBannerComponent> banner)
    {
        var xform = Transform(banner);
        if (!TryResolveBannerGrid(xform, out var grid))
            return;

        TryUnclaimFromGrid(banner, grid);
    }

    private void TryUnclaimFromGrid(Entity<TerritoryBannerComponent> banner, EntityUid grid)
    {
        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

        if (terr.ActiveClaimBanner != banner.Owner)
            return;

        // Clear to neutral.
        ClearActiveBannerBlip(banner.Owner);
        _territory.ClearController(grid);

        _popup.PopupEntity(Loc.GetString("grid-territory-unclaimed"), banner);
    }

    private void ConfigureActiveBannerBlip(
        Entity<TerritoryBannerComponent> banner,
        Entity<GridTerritoryComponent> territory)
    {
        EnsureComp<PhysicsComponent>(banner);

        var marker = EnsureComp<ActiveTerritoryBannerRadarBlipComponent>(banner);
        marker.Grid = territory.Owner;
        marker.Removing = false;

        var blip = EnsureComp<RadarBlipComponent>(banner);
        blip.Enabled = true;
        blip.RequireNoGrid = false;
        blip.VisibleFromOtherGrids = true;
        blip.MaxDistance = territory.Comp.Radius + ActiveBannerRadarEdgeVisibilityPadding;
        blip.GridConfig = null;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(
                -ActiveBannerRadarBlipHalfSize,
                -ActiveBannerRadarBlipHalfSize,
                ActiveBannerRadarBlipHalfSize,
                ActiveBannerRadarBlipHalfSize),
            Color = Color.White,
            Shape = RadarBlipShape.Square,
            RespectZoom = true,
            Rotate = false,
        };
    }

    private void ClearActiveBannerBlip(EntityUid banner)
    {
        if (!TryComp<ActiveTerritoryBannerRadarBlipComponent>(banner, out var marker) ||
            marker.Removing)
        {
            return;
        }

        marker.Removing = true;

        RemCompDeferred<ActiveTerritoryBannerRadarBlipComponent>(banner);

        if (HasComp<RadarBlipComponent>(banner))
            RemCompDeferred<RadarBlipComponent>(banner);
    }

    private void ClearPendingClaimActor(EntityUid banner, PendingTerritoryClaimActorComponent? pendingActor = null)
    {
        if (!Resolve(banner, ref pendingActor, false))
            return;

        if (pendingActor.LifeStage >= ComponentLifeStage.Stopping)
            return;

        RemCompDeferred<PendingTerritoryClaimActorComponent>(banner);
    }

    // Exodus start - tolerate late GridUid init for banners parented directly to the grid.
    private bool TryResolveBannerGrid(TransformComponent xform, out EntityUid grid)
    {
        if (xform.GridUid is { } gridUid)
        {
            grid = gridUid;
            return true;
        }

        if (HasComp<MapGridComponent>(xform.ParentUid))
        {
            grid = xform.ParentUid;
            return true;
        }

        grid = default;
        return false;
    }
    // Exodus end
}
