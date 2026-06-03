using Content.Shared._Exodus.Territory;
using Content.Shared.Construction; // for potential future
using Content.Server._Exodus.Territory; // for the marker sync (same logical area)
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Core system for grid-attached territory / influence zones.
/// Handles logical control state (which faction claims the grid) and keeps the
/// graphical marker (TerritoryMarkerComponent + RadarBlip) in sync.
/// 
/// Banners are one claim source (see GridTerritoryBannerSystem).
/// Other sources can call SetController directly.
/// 
/// Factions and their banners/labels are declared in TerritoryFactionPrototype (data-driven config).
/// Radius is a free float (common station values: 1000, 2500, 5000).
/// 
/// All new territory control code lives under _Exodus as per project style.
/// </summary>
public sealed class GridTerritorySystem : EntitySystem
{
    [Dependency] private readonly TerritoryMarkerSystem _marker = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridTerritoryComponent, ComponentStartup>(OnGridTerritoryStartup);
        // Future: could subscribe to changes if we use dirty or a directed event.
    }

    private void OnGridTerritoryStartup(Entity<GridTerritoryComponent> ent, ref ComponentStartup args)
    {
        EnsureVisual(ent);
        Dirty(ent, ent.Comp); // # Exodus - ensure initial prototype values (e.g. Radius) are sent to clients for map icon logic etc.
    }

    /// <summary>
    /// Ensures the grid has a TerritoryMarkerComponent with current radius and effective label,
    /// then syncs the radar blip.
    /// Label is resolved from the TerritoryFactionPrototype if a faction is set.
    /// </summary>
    private void EnsureVisual(Entity<GridTerritoryComponent> ent)
    {
        var marker = EnsureComp<TerritoryMarkerComponent>(ent);

        marker.Radius = ent.Comp.Radius;

        LocId label = ent.Comp.DefaultLabel;

        if (ent.Comp.ControllingFaction is { } factionId &&
            _proto.TryIndex(factionId, out var factionProto))
        {
            label = factionProto.RadarLabel;
        }

        marker.Text = label;

        _marker.SyncBlip((ent.Owner, marker));
    }

    /// <summary>
    /// Sets (or clears) the controlling faction for a grid's territory.
    /// 
    /// This is the central API for claim changes:
    /// - Primarily called by GridTerritoryBannerSystem (when banner anchored/removed).
    /// - Can be called by future systems (capture mechanics, events, admin commands, etc.).
    /// 
    /// Per design: stations have no default faction ownership. 
    /// Initial claims should come from physical banners placed in the map (the banner system
    /// will set it on load via this method).
    /// 
    /// The radar label is resolved from the TerritoryFactionPrototype (by its radarLabel LocId),
    /// never passed directly. When faction is null → uses the component's defaultLabel (usually "Незанято").
    /// 
    /// Changes are purely runtime on the grid entity (not persisted to yaml).
    /// 
    /// sourceBanner tracks which banner is providing the current claim (for single-banner enforcement).
    /// </summary>
    public void SetController(
        EntityUid grid,
        ProtoId<TerritoryFactionPrototype>? faction,
        EntityUid? sourceBanner = null)
    {
        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

        var oldFaction = terr.ControllingFaction;

        terr.ControllingFaction = faction;
        terr.ActiveClaimBanner = sourceBanner;

        Dirty(grid, terr); // # Exodus - ensure Radius etc replicated to client for map icons etc.

        // Resolve the label from the prototype (or fall back to default for neutral)
        LocId effectiveLabel = terr.DefaultLabel;
        if (faction is { } factionId && _proto.TryIndex(factionId, out var factionProto))
        {
            effectiveLabel = factionProto.RadarLabel;
        }

        // Update the visual marker's text/radius and ensure blip is refreshed.
        if (TryComp<TerritoryMarkerComponent>(grid, out var marker))
        {
            marker.Text = effectiveLabel;
            marker.Radius = terr.Radius;
            _marker.SyncBlip((grid, marker));
        }
        else
        {
            // Ensure visual if it wasn't present (e.g. set via yaml or admin).
            EnsureVisual((grid, terr));
        }

        // Extensibility hook for future capture mechanics, alerts, etc.
        var ev = new GridTerritoryControllerChangedEvent(grid, oldFaction, faction, sourceBanner);
        RaiseLocalEvent(grid, ref ev);
    }

    /// <summary>
    /// Convenience for neutral/unclaimed state.
    /// </summary>
    public void ClearController(EntityUid grid)
    {
        SetController(grid, null, null);
    }
}
