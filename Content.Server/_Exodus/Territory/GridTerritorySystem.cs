using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction; // for potential future
using Content.Shared._Crescent.SpaceBiomes;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Numerics;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Core system for grid-attached territory / influence zones.
/// Handles logical control state (which faction claims the grid) and keeps the
/// graphical marker (TerritoryMarkerComponent + RadarBlip) in sync.
/// 
/// Banners are one claim source (see GridTerritoryBannerSystem).
/// Other sources can call SetController directly.
/// 
/// Factions and their banners/labels are declared in TerritoryFactionPrototype.
/// Territory sizes and POI coloring rules are declared in TerritoryProfilePrototype.
/// 
/// All new territory control code lives under _Exodus as per project style.
/// </summary>
public sealed partial class GridTerritorySystem : EntitySystem
{
    [Dependency] private TerritoryMarkerSystem _marker = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private GridTerritoryNpcFactionSystem _npcFaction = default!;
    [Dependency] private TerritoryClaimRulesSystem _claimRules = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridTerritoryComponent, ComponentStartup>(OnGridTerritoryStartup);
        SubscribeLocalEvent<GridTerritoryComponent, ComponentShutdown>(OnGridTerritoryShutdown);
        // Future: could subscribe to changes if we use dirty or a directed event.
    }

    private void OnGridTerritoryStartup(Entity<GridTerritoryComponent> ent, ref ComponentStartup args)
    {
        ApplyProfile(ent);
        EnsureVisual(ent);
        EnsureTerritoryBiomeSource(ent);
        Dirty(ent, ent.Comp); // # Exodus - ensure profile-derived values are sent to clients for map icon logic etc.
    }

    private void OnGridTerritoryShutdown(Entity<GridTerritoryComponent> ent, ref ComponentShutdown args)
    {
        DeleteTerritoryBiomeSource(ent);
    }

    private void ApplyProfile(Entity<GridTerritoryComponent> ent)
    {
        var profile = ResolveProfile(ent);
        if (profile == null)
            return;

        ent.Comp.Radius = profile.Radius;
        ent.Comp.BiomeSourcePrototype = profile.BiomeSourcePrototype;
        ent.Comp.Claimable = profile.Claimable;
        ent.Comp.MinClaimRepairIntegrity =
            profile.MinClaimRepairIntegrity ?? _claimRules.GetDefaultMinClaimRepairIntegrity();
        ent.Comp.ColorPoiByFaction = profile.ColorPoiByFaction;
        ent.Comp.NeutralPoiColor = profile.NeutralPoiColor;
    }

    private TerritoryProfilePrototype? ResolveProfile(Entity<GridTerritoryComponent> ent)
    {
        var gameMapId = GetGameMapId(ent.Owner);
        if (gameMapId != null)
        {
            var gameMapPrototype = new ProtoId<GameMapPrototype>(gameMapId);

            foreach (var profile in _proto.EnumeratePrototypes<TerritoryProfilePrototype>())
            {
                if (profile.GameMapPrototypes.Contains(gameMapPrototype))
                    return profile;
            }
        }

        TerritoryProfilePrototype? defaultProfile = null;

        foreach (var profile in _proto.EnumeratePrototypes<TerritoryProfilePrototype>())
        {
            if (!profile.Default)
                continue;

            if (defaultProfile != null)
            {
                Log.Error($"Multiple default territory profiles configured: {defaultProfile.ID} and {profile.ID}.");
                return defaultProfile;
            }

            defaultProfile = profile;
        }

        if (defaultProfile == null)
            Log.Error($"GridTerritory on {ToPrettyString(ent)} has no matching or default territory profile.");

        return defaultProfile;
    }

    private string? GetGameMapId(EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is { } station &&
            TryComp<StationDataComponent>(station, out var stationData) &&
            stationData.StationConfig != null)
        {
            foreach (var gameMap in _proto.EnumeratePrototypes<GameMapPrototype>())
            {
                foreach (var (_, stationConfig) in gameMap.Stations)
                {
                    if (ReferenceEquals(stationConfig, stationData.StationConfig))
                        return gameMap.ID;
                }
            }
        }

        if (TryComp<BecomesStationComponent>(grid, out var becomesStation))
            return becomesStation.Id;

        return MetaData(grid).EntityPrototype?.ID;
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

        // # Exodus start - apply faction color to territory rings (BSS map + nav radar)
        // Main claim factions: TSFMC, PDV, Khsira. Side claim faction: Syndicate.
        if (ent.Comp.ControllingFaction is { } factionId &&
            _proto.TryIndex(factionId, out var factionProto))
        {
            label = factionProto.RadarLabel;
            marker.FillColor = factionProto.Color.WithAlpha(0.02f);
            marker.BorderColor = factionProto.Color.WithAlpha(0.28f);
        }
        else
        {
            // Unclaimed / neutral territory
            marker.FillColor = new Color(0.65f, 0.65f, 0.65f, 0.02f);
            marker.BorderColor = new Color(0.70f, 0.70f, 0.70f, 0.085f);
        }
        // # Exodus end - faction color for rings

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
    /// never passed directly. When faction is null, uses the component's defaultLabel.
    /// 
    /// Changes are purely runtime on the grid entity (not persisted to yaml).
    /// 
    /// sourceBanner tracks which banner is providing the current claim (for single-banner enforcement).
    /// </summary>
    public void SetController(
        EntityUid grid,
        ProtoId<TerritoryFactionPrototype>? faction,
        EntityUid? sourceBanner = null,
        EntityUid? actor = null)
    {
        if (!TryComp<GridTerritoryComponent>(grid, out var terr))
            return;

        ApplyProfile((grid, terr));

        if (!terr.Claimable && faction != null)
            return;

        var oldFaction = terr.ControllingFaction;
        var oldClaimBanner = terr.ActiveClaimBanner;
        var controllerChanged =
            !EqualityComparer<ProtoId<TerritoryFactionPrototype>?>.Default.Equals(oldFaction, faction) ||
            oldClaimBanner != sourceBanner;

        terr.ControllingFaction = faction;
        terr.ActiveClaimBanner = sourceBanner;

        Dirty(grid, terr); // # Exodus - ensure profile-derived values are replicated to client for map icons etc.

        // Resolve the label from the prototype (or fall back to default for neutral)
        LocId effectiveLabel = terr.DefaultLabel;
        TerritoryFactionPrototype? factionProto = null;
        if (faction is { } factionId && _proto.TryIndex(factionId, out factionProto))
        {
            effectiveLabel = factionProto.RadarLabel;
        }

        // Update the visual marker's text/radius and ensure blip is refreshed.
        if (TryComp<TerritoryMarkerComponent>(grid, out var marker))
        {
            marker.Text = effectiveLabel;
            marker.Radius = terr.Radius;

            // # Exodus start - apply faction color to territory rings (BSS map + nav radar)
            if (factionProto != null)
            {
                marker.FillColor = factionProto.Color.WithAlpha(0.02f);
                marker.BorderColor = factionProto.Color.WithAlpha(0.28f);
            }
            else
            {
                // Unclaimed
                marker.FillColor = new Color(0.65f, 0.65f, 0.65f, 0.02f);
                marker.BorderColor = new Color(0.70f, 0.70f, 0.70f, 0.085f);
            }
            // # Exodus end - faction color for rings

            _marker.SyncBlip((grid, marker));
        }
        else
        {
            // Ensure visual if it wasn't present (e.g. set via yaml or admin).
            EnsureVisual((grid, terr));
        }

        // Extensibility hook for future capture mechanics, alerts, etc.
        if (!controllerChanged)
            return;

        _npcFaction.SyncControllerFaction(grid, faction);

        var ev = new GridTerritoryControllerChangedEvent(grid, oldFaction, faction, oldClaimBanner, sourceBanner, actor);
        RaiseLocalEvent(grid, ref ev, true);
    }

    /// <summary>
    /// Convenience for neutral/unclaimed state.
    /// </summary>
    public void ClearController(EntityUid grid, EntityUid? actor = null)
    {
        SetController(grid, null, null, actor);
    }

    /// <summary>
    /// Spawns a configured biome source entity as a child of the grid.
    /// The source prototype owns biome id, priority and visibility flags; territory radius only controls its swap distance.
    /// </summary>
    private void EnsureTerritoryBiomeSource(Entity<GridTerritoryComponent> ent)
    {
        if (ent.Comp.BiomeSourcePrototype is not { } sourcePrototype)
        {
            DeleteTerritoryBiomeSource(ent);
            return;
        }

        if (!_proto.HasIndex<EntityPrototype>(sourcePrototype))
        {
            Log.Error($"GridTerritory on {ToPrettyString(ent)} references missing biome source prototype {sourcePrototype}.");
            DeleteTerritoryBiomeSource(ent);
            return;
        }

        ClearInvalidActiveBiomeSource(ent, sourcePrototype);

        if (TryGetExistingTerritoryBiomeSource(ent, sourcePrototype, out var existingSource, out var existingSourceComp))
        {
            ent.Comp.ActiveBiomeSource = existingSource;
            existingSourceComp!.SwapDistance = ent.Comp.Radius;
            Dirty(existingSource, existingSourceComp);
            return;
        }

        var sourceUid = Spawn(sourcePrototype, new EntityCoordinates(ent.Owner, Vector2.Zero));

        if (!TryComp<SpaceBiomeSourceComponent>(sourceUid, out var sourceComp))
        {
            Log.Error($"Territory biome source prototype {sourcePrototype} has no {nameof(SpaceBiomeSourceComponent)}.");
            QueueDel(sourceUid);
            return;
        }

        ent.Comp.ActiveBiomeSource = sourceUid;
        sourceComp.SwapDistance = ent.Comp.Radius;
        Dirty(sourceUid, sourceComp);
    }

    private void ClearInvalidActiveBiomeSource(Entity<GridTerritoryComponent> ent, ProtoId<EntityPrototype> sourcePrototype)
    {
        if (ent.Comp.ActiveBiomeSource is not { } activeSource)
            return;

        if (TerminatingOrDeleted(activeSource))
        {
            ent.Comp.ActiveBiomeSource = null;
            return;
        }

        if (!TryComp<SpaceBiomeSourceComponent>(activeSource, out _) ||
            Transform(activeSource).ParentUid != ent.Owner ||
            MetaData(activeSource).EntityPrototype?.ID != sourcePrototype.Id)
        {
            QueueDel(activeSource);
            ent.Comp.ActiveBiomeSource = null;
        }
    }

    private bool TryGetExistingTerritoryBiomeSource(
        Entity<GridTerritoryComponent> ent,
        ProtoId<EntityPrototype> sourcePrototype,
        out EntityUid sourceUid,
        out SpaceBiomeSourceComponent? sourceComp)
    {
        sourceUid = default;
        sourceComp = null;

        var activeSource = ent.Comp.ActiveBiomeSource;
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform, out var meta))
        {
            if (xform.ParentUid != ent.Owner ||
                meta.EntityPrototype?.ID != sourcePrototype.Id)
            {
                continue;
            }

            if (activeSource is { } active && active == uid)
            {
                if (sourceComp != null && sourceUid != uid)
                    QueueDel(sourceUid);

                sourceUid = uid;
                sourceComp = comp;
                continue;
            }

            if (sourceComp == null)
            {
                sourceUid = uid;
                sourceComp = comp;
                continue;
            }

            QueueDel(uid);
        }

        return sourceComp != null;
    }

    private void DeleteTerritoryBiomeSource(Entity<GridTerritoryComponent> ent)
    {
        var activeSource = ent.Comp.ActiveBiomeSource;

        if (activeSource is { } sourceToDelete && !TerminatingOrDeleted(sourceToDelete))
            QueueDel(sourceToDelete);

        ent.Comp.ActiveBiomeSource = null;

        if (ent.Comp.BiomeSourcePrototype is not { } sourcePrototype)
            return;

        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if ((activeSource is { } sourceToSkip && uid == sourceToSkip) ||
                xform.ParentUid != ent.Owner ||
                meta.EntityPrototype?.ID != sourcePrototype.Id)
            {
                continue;
            }

            QueueDel(uid);
        }
    }
}
