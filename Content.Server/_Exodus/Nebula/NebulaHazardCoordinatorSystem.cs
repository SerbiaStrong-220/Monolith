using Content.Shared._Exodus.Nebula;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Reactive dispatcher for nebula effects. Listens to <see cref="NebulaPresenceChangedEvent"/>
/// and adds or removes the relevant per-effect components on the entity. Per-effect systems
/// (lightning, EMP, radio blackout) only iterate entities that actually carry their component
/// instead of polling every player and grid each tick.
///
/// Also caches "marker prototype has component X" results so per-effect lookups are O(1).
/// </summary>
public sealed class NebulaHazardCoordinatorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private readonly HashSet<string> _ftlBlockerMarkers = new();
    private readonly HashSet<string> _lightningMarkers = new();
    private readonly HashSet<string> _spaceLightningMarkers = new();
    private readonly HashSet<string> _empMarkers = new();
    private readonly HashSet<string> _spaceEmpMarkers = new();
    private readonly HashSet<string> _radioBlackoutMarkers = new();
    private readonly HashSet<string> _thrustReductionMarkers = new();

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
    private bool MarkerHasThrustReduction(EntProtoId marker) => marker.Id != null && _thrustReductionMarkers.Contains(marker.Id);

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
        _thrustReductionMarkers.Clear();

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
            if (proto.TryGetComponent<NebulaThrustReductionComponent>(out _, _componentFactory))
                _thrustReductionMarkers.Add(proto.ID);
        }
    }

    private void OnPresenceChanged(EntityUid uid, NebulaPresenceComponent comp, ref NebulaPresenceChangedEvent ev)
    {
        ApplyEffects(uid, ev.NewMarker);
    }

    private void OnPresenceRemoved(EntityUid uid, NebulaPresenceComponent component, ComponentRemove args)
    {
        ApplyEffects(uid, default);
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
            UpdateSpaceLightning(uid, !skipPlayerHazards && MarkerHasSpaceLightning(marker));
        }

        // EMP hazard: grids and free entities (EVA) have independent components on the marker
        // so they can be enabled separately per nebula kind (symmetric to lightning).
        if (isGrid)
        {
            UpdateGridEmp(uid, !skipPlayerHazards && MarkerHasEmp(marker), marker);
        }
        else
        {
            UpdateSpaceEmp(uid, !skipPlayerHazards && MarkerHasSpaceEmp(marker));
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
            // Marker change implies a new tier configuration; clear the existing strike
            // schedule so InitializeGridTimers reapplies the new intervals on the next tick.
            // Statistics (LastStrike, StrikeCount) are intentionally preserved.
            if (hazard.Marker != marker)
            {
                hazard.Marker = marker;
                hazard.TimersInitialized = false;
                hazard.NextSmallStrike = default;
                hazard.NextHeavyStrike = default;
                hazard.NextSuperHeavyStrike = default;
            }
        }
        else if (HasComp<NebulaLightningGridHazardComponent>(uid))
        {
            RemCompDeferred<NebulaLightningGridHazardComponent>(uid);
        }
    }

    private void UpdateSpaceLightning(EntityUid uid, bool wanted)
    {
        if (wanted)
            EnsureComp<NebulaSpaceLightningTargetComponent>(uid);
        else if (HasComp<NebulaSpaceLightningTargetComponent>(uid))
            RemCompDeferred<NebulaSpaceLightningTargetComponent>(uid);
    }

    private void UpdateGridEmp(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var hazard = EnsureComp<NebulaEmpGridHazardComponent>(uid);
            // Marker change implies a new EMP config; clear the next-pulse schedule so the
            // system reapplies the new delay range on the next tick. Statistics preserved.
            if (hazard.Marker != marker)
            {
                hazard.Marker = marker;
                hazard.TimersInitialized = false;
                hazard.NextPulse = default;
            }
        }
        else if (HasComp<NebulaEmpGridHazardComponent>(uid))
        {
            RemCompDeferred<NebulaEmpGridHazardComponent>(uid);
        }
    }

    private void UpdateSpaceEmp(EntityUid uid, bool wanted)
    {
        if (wanted)
            EnsureComp<NebulaSpaceEmpTargetComponent>(uid);
        else if (HasComp<NebulaSpaceEmpTargetComponent>(uid))
            RemCompDeferred<NebulaSpaceEmpTargetComponent>(uid);
    }

    private void UpdateRadioBlackout(EntityUid uid, bool wanted)
    {
        if (wanted)
            EnsureComp<NebulaRadioBlackoutComponent>(uid);
        else if (HasComp<NebulaRadioBlackoutComponent>(uid))
            RemCompDeferred<NebulaRadioBlackoutComponent>(uid);
    }
}
