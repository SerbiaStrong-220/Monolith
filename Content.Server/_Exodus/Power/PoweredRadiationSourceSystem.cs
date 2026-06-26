using Content.Server.Power.EntitySystems;
using Content.Server.Radiation.Systems;
using Content.Shared._Exodus.Power.Components;
using Content.Shared.Power;
using Content.Shared.Radiation.Components;

namespace Content.Server._Exodus.Power;

/// <summary>
/// Gates RadiationSource by both machine power and explicit active state.
/// Works with any machine that has ApcPowerReceiver + RadiationSource + PoweredRadiationSourceComponent.
/// </summary>
public sealed partial class PoweredRadiationSourceSystem : EntitySystem
{
    [Dependency] private RadiationSystem _radiation = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PoweredRadiationSourceComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<PoweredRadiationSourceComponent, ComponentShutdown>(OnShutdown);
    }

    public void SetActive(EntityUid uid, bool active, PoweredRadiationSourceComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.Active = active;
        UpdateSource(uid, component);
    }

    private void OnPowerChanged(Entity<PoweredRadiationSourceComponent> ent, ref PowerChangedEvent args)
    {
        UpdateSource(ent.Owner, ent.Comp, args.Powered);
    }

    private void OnShutdown(Entity<PoweredRadiationSourceComponent> ent, ref ComponentShutdown args)
    {
        _radiation.SetSourceEnabled(ent.Owner, false);
    }

    private void UpdateSource(EntityUid uid, PoweredRadiationSourceComponent component, bool? powered = null)
    {
        var isPowered = powered ?? this.IsPowered(uid, EntityManager);
        _radiation.SetSourceEnabled(uid, component.Active && isPowered);
    }
}
