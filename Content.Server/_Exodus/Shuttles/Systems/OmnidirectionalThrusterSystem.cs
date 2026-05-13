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

    public override void Initialize()
    {
        base.Initialize();
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

    private void EnableOmni(EntityUid uid, ThrusterComponent thruster)
    {
        if (thruster.IsOn)
            return;

        var xform = Transform(uid);
        if (!TryComp<ShuttleComponent>(xform.GridUid, out var shuttle))
            return;

        thruster.IsOn = true;

        for (var i = 0; i < 4; i++)
        {
            shuttle.LinearThrust[i] += thruster.Thrust;
            shuttle.BaseLinearThrust[i] += thruster.BaseThrust;
            shuttle.LinearThrusters[i].Add(uid);
        }

        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, ThrusterVisualState.State, true, appearance);

        if (_light.TryGetLight(uid, out var light))
            _light.SetEnabled(uid, true, light);

        _ambient.SetAmbience(uid, true);
        _poweredRadiation.SetActive(uid, true);
    }

    private void DisableOmni(EntityUid uid, ThrusterComponent thruster)
    {
        // Always kill effects regardless of IsOn state
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, ThrusterVisualState.State, false, appearance);

        if (_light.TryGetLight(uid, out var light))
            _light.SetEnabled(uid, false, light);

        _ambient.SetAmbience(uid, false);
        _poweredRadiation.SetActive(uid, false);

        if (!thruster.IsOn)
            return;

        thruster.IsOn = false;

        var xform = Transform(uid);
        if (!TryComp<ShuttleComponent>(xform.GridUid, out var shuttle))
            return;

        for (var i = 0; i < 4; i++)
        {
            shuttle.LinearThrust[i] -= thruster.Thrust;
            shuttle.BaseLinearThrust[i] -= thruster.BaseThrust;
            shuttle.LinearThrusters[i].Remove(uid);
        }
    }
}
