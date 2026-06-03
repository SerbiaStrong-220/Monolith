using Content.Server._Exodus.Power;
using Content.Server._Exodus.Shuttles.Components;
using Content.Server.Audio;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Shuttles.Systems;

/// <summary>
/// Handles thrusters that provide linear thrust in all 4 directions simultaneously.
/// Each direction receives the full Thrust value from ThrusterComponent.
/// </summary>
public sealed class OmnidirectionalThrusterSystem : EntitySystem
{
    [Dependency] private readonly AmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly PoweredRadiationSourceSystem _poweredRadiation = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Claim standard thruster handling: ThrusterSystem skips its default logic for us.
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, ThrusterHandledExternallyEvent>(OnHandledExternally);
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, PowerChangedEvent>(OnPowerChanged);
        // After ThrusterSystem sets Enabled, we re-evaluate our state
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, ActivateInWorldEvent>(OnActivate,
            after: [typeof(ThrusterSystem)]);
        SubscribeLocalEvent<OmnidirectionalThrusterComponent, SignalReceivedEvent>(OnSignalReceived,
            after: [typeof(ThrusterSystem)]);
    }

    private void OnHandledExternally(Entity<OmnidirectionalThrusterComponent> ent, ref ThrusterHandledExternallyEvent args)
    {
        args.Handled = true;
    }

    private void OnShutdown(Entity<OmnidirectionalThrusterComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;
        DisableOmni(ent, thruster);
    }

    private void OnMapInit(Entity<OmnidirectionalThrusterComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;

        if (CanEnableOmni(ent, thruster))
            EnableOmni(ent, thruster);
        else
            DisableOmni(ent, thruster);
    }

    private void OnActivate(Entity<OmnidirectionalThrusterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;

        // ThrusterSystem already toggled Enabled - sync power load and re-evaluate
        SyncPowerLoad(ent, thruster);

        if (CanEnableOmni(ent, thruster))
            EnableOmni(ent, thruster);
        else
            DisableOmni(ent, thruster);
    }

    private void OnSignalReceived(Entity<OmnidirectionalThrusterComponent> ent, ref SignalReceivedEvent args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;

        SyncPowerLoad(ent, thruster);

        if (CanEnableOmni(ent, thruster))
            EnableOmni(ent, thruster);
        else
            DisableOmni(ent, thruster);
    }

    private void SyncPowerLoad(EntityUid uid, ThrusterComponent thruster)
    {
        if (!TryComp<ApcPowerReceiverComponent>(uid, out var apcPower) || thruster.OriginalLoad == 0)
            return;

        if (!thruster.Enabled)
            apcPower.Load = 1;
        else if (apcPower.Load != thruster.OriginalLoad)
            apcPower.Load = thruster.OriginalLoad;
    }

    private void OnAnchorChanged(Entity<OmnidirectionalThrusterComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;

        if (args.Anchored && CanEnableOmni(ent, thruster))
            EnableOmni(ent, thruster);
        else
            DisableOmni(ent, thruster);
    }

    private void OnPowerChanged(Entity<OmnidirectionalThrusterComponent> ent, ref PowerChangedEvent args)
    {
        if (!TryComp<ThrusterComponent>(ent, out var thruster))
            return;

        if (args.Powered && CanEnableOmni(ent, thruster))
            EnableOmni(ent, thruster);
        else
            DisableOmni(ent, thruster);
    }

    private bool CanEnableOmni(EntityUid uid, ThrusterComponent thruster)
    {
        if (!thruster.Enabled)
            return false;

        if (thruster.LifeStage > ComponentLifeStage.Running)
            return false;

        var xform = Transform(uid);
        return xform.Anchored && this.IsPowered(uid, EntityManager);
    }

    private void EnableOmni(Entity<OmnidirectionalThrusterComponent> ent, ThrusterComponent thruster)
    {
        if (thruster.IsOn)
            return;

        var xform = Transform(ent.Owner);
        if (!TryComp<ShuttleComponent>(xform.GridUid, out var shuttle))
            return;

        thruster.IsOn = true;
        ent.Comp.CurrentGrid = xform.GridUid;

        for (var i = 0; i < 4; i++)
            _thruster.AddLinearThrust(shuttle, i, thruster.Thrust, thruster.BaseThrust, ent.Owner);

        if (TryComp<AppearanceComponent>(ent.Owner, out var appearance))
            _appearance.SetData(ent.Owner, ThrusterVisualState.State, true, appearance);

        if (_light.TryGetLight(ent.Owner, out var light))
            _light.SetEnabled(ent.Owner, true, light);

        _ambient.SetAmbience(ent.Owner, true);
        _poweredRadiation.SetActive(ent.Owner, true);
    }

    private void DisableOmni(Entity<OmnidirectionalThrusterComponent> ent, ThrusterComponent thruster)
    {
        // Always kill effects regardless of IsOn state
        if (TryComp<AppearanceComponent>(ent.Owner, out var appearance))
            _appearance.SetData(ent.Owner, ThrusterVisualState.State, false, appearance);

        if (_light.TryGetLight(ent.Owner, out var light))
            _light.SetEnabled(ent.Owner, false, light);

        _ambient.SetAmbience(ent.Owner, false);
        _poweredRadiation.SetActive(ent.Owner, false);

        if (!thruster.IsOn)
            return;

        thruster.IsOn = false;

        // Use the grid the thrust was registered on, not the current xform (the thruster may have
        // been unanchored or moved to a different grid since EnableOmni).
        if (TryComp<ShuttleComponent>(ent.Comp.CurrentGrid, out var shuttle))
        {
            for (var i = 0; i < 4; i++)
                _thruster.RemoveLinearThrust(shuttle, i, thruster.Thrust, thruster.BaseThrust, ent.Owner);
        }

        ent.Comp.CurrentGrid = null;
    }
}
