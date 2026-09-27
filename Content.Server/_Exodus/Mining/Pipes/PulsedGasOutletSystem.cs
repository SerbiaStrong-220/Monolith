using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Atmos;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed class PulsedGasOutletSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IMapManager _maps = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PulsedGasOutletComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PulsedGasOutletComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<PulsedGasOutletComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<PulsedGasOutletComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PulsedGasOutletComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnMapInit(Entity<PulsedGasOutletComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextPulse = _timing.CurTime + ent.Comp.CycleInterval;
    }

    private void OnAnchorChanged(Entity<PulsedGasOutletComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized)
            return;

        ent.Comp.NextPulse = _timing.CurTime + ent.Comp.CycleInterval;
    }

    private void OnActivate(Entity<PulsedGasOutletComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        ent.Comp.Enabled = !ent.Comp.Enabled;
        ent.Comp.NextPulse = _timing.CurTime + ent.Comp.CycleInterval;
        Dirty(ent);
        args.Handled = true;
    }

    private void OnExamined(Entity<PulsedGasOutletComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Enabled ? "bulk-mining-exhaust-enabled" : "bulk-mining-exhaust-disabled",
            ("seconds", ent.Comp.CycleInterval.TotalSeconds)));
    }

    private void OnAtmosUpdate(Entity<PulsedGasOutletComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        var now = _timing.CurTime;
        if (!ent.Comp.Enabled || ent.Comp.CycleInterval <= TimeSpan.Zero || now < ent.Comp.NextPulse)
            return;

        // Preserve the cadence, but never replay missed bursts after a stalled atmos update.
        var intervals = (now - ent.Comp.NextPulse).Ticks / ent.Comp.CycleInterval.Ticks + 1;
        ent.Comp.NextPulse += TimeSpan.FromTicks(ent.Comp.CycleInterval.Ticks * intervals);
        TryDischarge(ent);
    }

    private bool TryDischarge(Entity<PulsedGasOutletComponent> ent)
    {
        var xform = Transform(ent);
        if (!xform.Anchored || xform.MapUid is not { } map ||
            !_nodes.TryGetNode(ent.Owner, ent.Comp.Inlet, out PipeNode? inlet) ||
            inlet.NodeGroup is not BaseNodeGroup { Removed: false, Remaking: false } ||
            inlet.Air.Immutable || inlet.Air.Temperature <= 0 || inlet.Air.TotalMoles <= 0)
            return false;

        var coordinates = new EntityCoordinates(ent, ent.Comp.OutletOffset);
        var outlet = _transform.ToMapCoordinates(coordinates);
        var environment = _maps.TryFindGridAt(outlet, out var grid, out var gridComp)
            ? _atmos.GetTileMixture(grid, map, _map.TileIndicesFor(grid, gridComp, coordinates), true)
            : _atmos.GetTileMixture(null, map, default, true);

        // Airtight tiles have no mixture. Keep gas in the pipe when the nozzle is obstructed.
        if (environment == null || environment.Pressure >= ent.Comp.MaxPressure)
            return false;

        var amount = Math.Min(ent.Comp.MolesPerPulse, inlet.Air.TotalMoles);
        if (!environment.Immutable)
        {
            var capacity = (ent.Comp.MaxPressure - environment.Pressure) * environment.Volume /
                           (Atmospherics.R * inlet.Air.Temperature);
            amount = Math.Min(amount, capacity);
        }

        if (!float.IsFinite(amount) || amount <= 0.001f)
            return false;

        _atmos.Merge(environment, inlet.Air.Remove(amount));
        ent.Comp.LastPulse = _timing.CurTime;
        Dirty(ent);
        _audio.PlayPvs(ent.Comp.DischargeSound, ent);
        return true;
    }
}
