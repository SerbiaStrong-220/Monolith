using Content.Server._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Events;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Hazards;

/// <summary>
/// Reactive dispatcher for nebula effects. Listens to <see cref="NebulaPresenceChangedEvent"/>
/// and adds or removes the relevant per-effect components on the entity. Per-effect systems
/// (lightning, EMP, radio blackout) only iterate entities that actually carry their component
/// instead of polling every player and grid each tick.
///
/// Also caches "marker prototype has component X" results so per-effect lookups are O(1).
/// </summary>
public sealed partial class NebulaHazardCoordinatorSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private readonly HashSet<string> _ftlBlockerMarkers = new();
    private readonly HashSet<string> _lightningMarkers = new();
    private readonly HashSet<string> _spaceLightningMarkers = new();
    private readonly HashSet<string> _empMarkers = new();
    private readonly HashSet<string> _spaceEmpMarkers = new();
    private readonly HashSet<string> _radioBlackoutMarkers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NebulaPresenceComponent, NebulaPresenceChangedEvent>(OnPresenceChanged);
        SubscribeLocalEvent<NebulaPresenceComponent, ComponentRemove>(OnPresenceRemoved);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildCache();
    }

    private bool MarkerBlocksFTL(EntProtoId marker) => marker.Id != null && _ftlBlockerMarkers.Contains(marker.Id);
    private bool MarkerHasLightning(EntProtoId marker) => marker.Id != null && _lightningMarkers.Contains(marker.Id);
    private bool MarkerHasSpaceLightning(EntProtoId marker) => marker.Id != null && _spaceLightningMarkers.Contains(marker.Id);
    private bool MarkerHasEmp(EntProtoId marker) => marker.Id != null && _empMarkers.Contains(marker.Id);
    private bool MarkerHasSpaceEmp(EntProtoId marker) => marker.Id != null && _spaceEmpMarkers.Contains(marker.Id);
    private bool MarkerHasRadioBlackout(EntProtoId marker) => marker.Id != null && _radioBlackoutMarkers.Contains(marker.Id);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            BuildCache();
    }

    private void BuildCache()
    {
        _ftlBlockerMarkers.Clear();
        _lightningMarkers.Clear();
        _spaceLightningMarkers.Clear();
        _empMarkers.Clear();
        _spaceEmpMarkers.Clear();
        _radioBlackoutMarkers.Clear();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<NebulaComponent>(out _, _componentFactory))
                continue;

            if (proto.TryGetComponent<NebulaFTLBlockerComponent>(out _, _componentFactory))
                _ftlBlockerMarkers.Add(proto.ID);
            if (proto.TryGetComponent<NebulaLightningHazardComponent>(out _, _componentFactory))
                _lightningMarkers.Add(proto.ID);
            if (proto.TryGetComponent<NebulaSpaceLightningHazardComponent>(out _, _componentFactory))
                _spaceLightningMarkers.Add(proto.ID);
            // EMP components honour their Enabled flag so disabled markers can still document
            // tuned values without firing the effect.
            if (proto.TryGetComponent<NebulaEmpHazardComponent>(out var empComp, _componentFactory) && empComp.Enabled)
                _empMarkers.Add(proto.ID);
            if (proto.TryGetComponent<NebulaSpaceEmpHazardComponent>(out var spaceEmpComp, _componentFactory) && spaceEmpComp.Enabled)
                _spaceEmpMarkers.Add(proto.ID);
            if (proto.TryGetComponent<NebulaRadioBlackoutSourceComponent>(out _, _componentFactory))
                _radioBlackoutMarkers.Add(proto.ID);
        }
    }

    private void OnPresenceChanged(Entity<NebulaPresenceComponent> ent, ref NebulaPresenceChangedEvent ev)
    {
        ApplyEffects(ent.Owner, ev.NewMarker);
    }

    private void OnPresenceRemoved(Entity<NebulaPresenceComponent> ent, ref ComponentRemove args)
    {
        ApplyEffects(ent.Owner, default);
    }

    private void ApplyEffects(EntityUid uid, EntProtoId marker)
    {
        var isGrid = HasComp<MapGridComponent>(uid);

        // Ghosts and dead bodies should not be hit by hazards. Their NebulaPresenceComponent
        // still exists (for parallax etc.), but no per-effect components get applied.
        var skipPlayerHazards = !isGrid && IsGhostOrDead(uid);

        // Lightning hazard: grids and free entities (EVA players) have independent components
        // on the marker prototype, so they can be enabled separately per nebula kind.
        if (isGrid)
        {
            UpdateGridLightning(uid, !skipPlayerHazards && MarkerHasLightning(marker), marker);
        }
        else
        {
            UpdateSpaceLightning(uid, !skipPlayerHazards && MarkerHasSpaceLightning(marker), marker);
        }

        // EMP hazard: grids and free entities (EVA) have independent components on the marker
        // so they can be enabled separately per nebula kind (symmetric to lightning).
        if (isGrid)
        {
            UpdateGridEmp(uid, !skipPlayerHazards && MarkerHasEmp(marker), marker);
        }
        else
        {
            UpdateSpaceEmp(uid, !skipPlayerHazards && MarkerHasSpaceEmp(marker), marker);
        }

        UpdateRadioBlackout(uid, !skipPlayerHazards && MarkerHasRadioBlackout(marker));
    }

    private bool IsGhostOrDead(EntityUid uid)
    {
        return HasComp<GhostComponent>(uid) || _mobState.IsDead(uid);
    }

    private void UpdateGridLightning(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var hazard = EnsureComp<NebulaLightningGridHazardComponent>(uid);
            if (hazard.Marker != marker)
            {
                ResetGridLightning(hazard, marker);
            }
        }
        else if (HasComp<NebulaLightningGridHazardComponent>(uid))
        {
            RemComp<NebulaLightningGridHazardComponent>(uid);
        }
    }

    private void UpdateSpaceLightning(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var target = EnsureComp<NebulaSpaceLightningTargetComponent>(uid);
            if (target.Marker != marker)
                ResetSpaceLightning(target, marker);
        }
        else if (HasComp<NebulaSpaceLightningTargetComponent>(uid))
        {
            RemComp<NebulaSpaceLightningTargetComponent>(uid);
        }
    }

    private void UpdateGridEmp(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var hazard = EnsureComp<NebulaEmpGridHazardComponent>(uid);
            if (hazard.Marker != marker)
            {
                ResetGridEmp(hazard, marker);
            }
        }
        else if (HasComp<NebulaEmpGridHazardComponent>(uid))
        {
            RemComp<NebulaEmpGridHazardComponent>(uid);
        }
    }

    private void UpdateSpaceEmp(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var target = EnsureComp<NebulaSpaceEmpTargetComponent>(uid);
            if (target.Marker != marker)
                ResetSpaceEmp(target, marker);
        }
        else if (HasComp<NebulaSpaceEmpTargetComponent>(uid))
        {
            RemComp<NebulaSpaceEmpTargetComponent>(uid);
        }
    }

    private void UpdateRadioBlackout(EntityUid uid, bool wanted)
    {
        if (wanted)
            EnsureComp<NebulaRadioBlackoutComponent>(uid);
        else if (HasComp<NebulaRadioBlackoutComponent>(uid))
            RemComp<NebulaRadioBlackoutComponent>(uid);
    }

    private static void ResetGridLightning(NebulaLightningGridHazardComponent hazard, EntProtoId marker)
    {
        hazard.Marker = marker;
        hazard.TimersInitialized = false;
        hazard.NextSmallStrike = default;
        hazard.NextHeavyStrike = default;
        hazard.NextSuperHeavyStrike = default;
        hazard.LastSmallStrike = default;
        hazard.LastHeavyStrike = default;
        hazard.LastSuperHeavyStrike = default;
        hazard.LastSmallDelta = default;
        hazard.LastHeavyDelta = default;
        hazard.LastSuperHeavyDelta = default;
        hazard.SmallStrikeCount = default;
        hazard.HeavyStrikeCount = default;
        hazard.SuperHeavyStrikeCount = default;
        hazard.CachedStrikeTiles.Clear();
        hazard.StrikeTileCacheInitialized = false;
        hazard.NextStrikeTileCacheRefresh = default;
    }

    private static void ResetSpaceLightning(NebulaSpaceLightningTargetComponent target, EntProtoId marker)
    {
        target.Marker = marker;
        target.NextStrike = default;
        target.LastStrike = default;
        target.StrikeCount = default;
    }

    private static void ResetGridEmp(NebulaEmpGridHazardComponent hazard, EntProtoId marker)
    {
        hazard.Marker = marker;
        hazard.TimersInitialized = false;
        hazard.NextPulse = default;
        hazard.LastPulse = default;
        hazard.LastPulseDelta = default;
        hazard.PulseCount = default;
        hazard.CachedPulseTiles.Clear();
        hazard.PulseTileCacheInitialized = false;
        hazard.NextPulseTileCacheRefresh = default;
    }

    private static void ResetSpaceEmp(NebulaSpaceEmpTargetComponent target, EntProtoId marker)
    {
        target.Marker = marker;
        target.NextPulse = default;
        target.LastPulse = default;
        target.PulseCount = default;
    }
}
