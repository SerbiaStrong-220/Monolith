using Content.Shared.Exodus.Stealth.Components;
using Content.Shared.Mobs;
using Robust.Shared.GameStates;

namespace Content.Shared.Exodus.Stealth;

public sealed partial class InstantStealthSystem : EntitySystem
{
    [Dependency] private readonly SharedStealthSystem _stealthSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstantStealthComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<InstantStealthComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<InstantStealthComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(EntityUid uid, InstantStealthComponent comp, MapInitEvent args)
    {
        if (!comp.Enabled)
            return;

        if (!_stealthSystem.RequestStealth(uid, nameof(InstantStealthSystem), comp.Stealth))
            return;
    }

    private void OnShutdown(EntityUid uid, InstantStealthComponent comp, ComponentShutdown args)
    {
        if (!_stealthSystem.RemoveRequest(nameof(InstantStealthSystem), uid))
            return;
    }

    private void OnMobStateChanged(EntityUid uid, InstantStealthComponent comp, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive && !comp.Stealth.EnabledOnCrit)
        {
            _stealthSystem.RequestStealth(uid, nameof(InstantStealthSystem), comp.Stealth);
        }
        else if (args.NewMobState == MobState.Alive && !comp.Stealth.EnabledOnDeath)
        {
            _stealthSystem.RequestStealth(uid, nameof(InstantStealthSystem), comp.Stealth);
        }
    }

    public void SetEnabled(EntityUid uid, bool value, InstantStealthComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        if (comp.Enabled == value)
            return;

        comp.Enabled = value;

        if (value)
            _stealthSystem.RequestStealth(uid, nameof(InstantStealthSystem), comp.Stealth);
        else
            _stealthSystem.RemoveRequest(nameof(InstantStealthSystem), uid);
    }

}
