using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Handles banners as a claim source for GridTerritory.
/// Enforces "only one active claim banner per grid" at runtime (construction condition handles build time).
/// When a qualifying banner is anchored on a grid with GridTerritoryComponent, it claims control
/// and the radar label updates to the faction name, or the neutral label when removed.
///
/// Factions without final art can use temporary placeholder banner entities.
/// </summary>
public sealed class GridTerritoryBannerSystem : EntitySystem
{
    [Dependency] private readonly GridTerritorySystem _territory = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();

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

        if (!_gridQuery.TryComp(territory.Owner, out var gridComp))
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

    private void OnAnchorChanged(Entity<TerritoryBannerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            TryClaim(ent);
        else
            TryUnclaim(ent);
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
        TryUnclaim(ent);
    }

    // OnConstructionChanged removed (event type may differ; anchor/parent/shutdown suffice for now).
    // # Exodus

    private void TryClaim(Entity<TerritoryBannerComponent> banner, bool showPopup = true)
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
            return;

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
        _territory.SetController(grid, banner.Comp.Faction, banner.Owner);

        if (showPopup)
            _popup.PopupEntity(Loc.GetString("grid-territory-claimed"), banner);
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
        _territory.ClearController(grid);

        _popup.PopupEntity(Loc.GetString("grid-territory-unclaimed"), banner);
    }

    // Exodus start - tolerate late GridUid init for anchored banners parented directly to the grid.
    private bool TryResolveBannerGrid(TransformComponent xform, out EntityUid grid)
    {
        if (xform.GridUid is { } gridUid)
        {
            grid = gridUid;
            return true;
        }

        if (xform.Anchored && _gridQuery.HasComponent(xform.ParentUid))
        {
            grid = xform.ParentUid;
            return true;
        }

        grid = default;
        return false;
    }
    // Exodus end
}