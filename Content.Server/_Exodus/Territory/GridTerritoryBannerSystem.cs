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
/// and the radar label updates to the faction name (or "Незанято" when removed).
/// 
/// Uses zaty chka / placeholder banners for factions without final art yet (Империя Кхси'Ра etc.).
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
        SubscribeLocalEvent<TerritoryBannerComponent, ComponentShutdown>(OnShutdown);
        // ConstructionChangedEvent subscription removed for initial implementation
        // (anchor/parent/shutdown cover unclaim on wrench/deconstruct). Add back with correct event if needed.
        // # Exodus - construction event sub commented for now
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
        // If the banner moves to a different grid (or off-grid), unclaim previous.
        TryUnclaim(ent);
        // If it landed on a new grid while anchored, the anchor event should fire, but we defensively check.
        var xform = Transform(ent);
        if (xform.Anchored)
            TryClaim(ent);
    }

    private void OnShutdown(Entity<TerritoryBannerComponent> ent, ref ComponentShutdown args)
    {
        TryUnclaim(ent);
    }

    // OnConstructionChanged removed (event type may differ; anchor/parent/shutdown suffice for now).
    // # Exodus

    private void TryClaim(Entity<TerritoryBannerComponent> banner)
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
            _popup.PopupEntity(Loc.GetString("grid-territory-already-claimed"), banner);
            // Do not claim; the new banner is physically there but does not grant control.
            // (Construction condition should have already prevented most cases.)
            return;
        }

        // Perform the claim. Label is resolved from the TerritoryFactionPrototype.
        _territory.SetController(grid, banner.Comp.Faction, banner.Owner);

        _popup.PopupEntity(Loc.GetString("grid-territory-claimed"), banner);
    }

    private void TryUnclaim(Entity<TerritoryBannerComponent> banner)
    {
        var xform = Transform(banner);
        if (xform.GridUid is not { } grid)
            return;

        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

        if (terr.ActiveClaimBanner != banner.Owner)
            return;

        // Clear to neutral.
        _territory.ClearController(grid);

        _popup.PopupEntity(Loc.GetString("grid-territory-unclaimed"), banner);
    }
}
