using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TerritoryBannerComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<TerritoryBannerComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<TerritoryBannerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TerritoryBannerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GridTerritoryComponent, GridTerritoryStartedEvent>(OnTerritoryStarted);
        // ConstructionChangedEvent subscription removed for initial implementation
        // (anchor/parent/shutdown cover unclaim on wrench/deconstruct). Add back with correct event if needed.
        // # Exodus - construction event sub commented for now
    }

    private void OnStartup(Entity<TerritoryBannerComponent> ent, ref ComponentStartup args)
    {
        if (Transform(ent).Anchored)
            TryClaim(ent, false);
    }

    private void OnTerritoryStarted(Entity<GridTerritoryComponent> ent, ref GridTerritoryStartedEvent args)
    {
        var query = EntityQueryEnumerator<TerritoryBannerComponent, TransformComponent>();
        while (query.MoveNext(out var bannerUid, out var banner, out var xform))
        {
            if (!xform.Anchored || xform.GridUid != ent.Owner)
                continue;

            TryClaim((bannerUid, banner), false);
        }
    }

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
        if (xform.GridUid is not { } grid)
            return;

        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

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
        if (xform.GridUid is not { } grid)
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
}

public readonly record struct GridTerritoryStartedEvent;
