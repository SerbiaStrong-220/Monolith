using Content.Shared._Exodus.Nebula;
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

    private readonly HashSet<string> _ftlBlockerMarkers = new();
    private readonly HashSet<string> _lightningMarkers = new();
    private readonly HashSet<string> _empMarkers = new();
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

    public bool MarkerBlocksFTL(EntProtoId marker) => marker.Id != null && _ftlBlockerMarkers.Contains(marker.Id);
    public bool MarkerHasLightning(EntProtoId marker) => marker.Id != null && _lightningMarkers.Contains(marker.Id);
    public bool MarkerHasEmp(EntProtoId marker) => marker.Id != null && _empMarkers.Contains(marker.Id);
    public bool MarkerHasRadioBlackout(EntProtoId marker) => marker.Id != null && _radioBlackoutMarkers.Contains(marker.Id);
    public bool MarkerHasThrustReduction(EntProtoId marker) => marker.Id != null && _thrustReductionMarkers.Contains(marker.Id);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            BuildCache();
    }

    private void BuildCache()
    {
        _ftlBlockerMarkers.Clear();
        _lightningMarkers.Clear();
        _empMarkers.Clear();
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
            if (proto.TryGetComponent<NebulaEmpHazardComponent>(out _, _componentFactory))
                _empMarkers.Add(proto.ID);
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

        // Lightning hazard: grids get the timer-bearing component, free entities (EVA players)
        // get the player-target component which the lightning system updates differently.
        var wantLightning = MarkerHasLightning(marker);
        if (isGrid)
        {
            UpdateGridLightning(uid, wantLightning, marker);
        }
        else
        {
            UpdateSpaceLightning(uid, wantLightning);
        }

        var wantEmp = MarkerHasEmp(marker);
        if (isGrid)
        {
            UpdateGridEmp(uid, wantEmp, marker);
        }
        else
        {
            UpdateSpaceEmp(uid, wantEmp);
        }

        UpdateRadioBlackout(uid, MarkerHasRadioBlackout(marker));
    }

    private void UpdateGridLightning(EntityUid uid, bool wanted, EntProtoId marker)
    {
        if (wanted)
        {
            var hazard = EnsureComp<NebulaLightningGridHazardComponent>(uid);
            hazard.Marker = marker;
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
            hazard.Marker = marker;
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
