using Content.Server.Radiation.Systems;
using Content.Shared._Exodus.Power.Components;
using Content.Shared.Power;
using Content.Shared.Radiation.Components;

namespace Content.Server._Exodus.Power;

/// <summary>
/// Enables or disables RadiationSource based on whether the machine is powered.
/// Works with any machine that has ApcPowerReceiver + RadiationSource + PoweredRadiationSourceComponent.
/// </summary>
public sealed class PoweredRadiationSourceSystem : EntitySystem
{
    [Dependency] private readonly RadiationSystem _radiation = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PoweredRadiationSourceComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnPowerChanged(Entity<PoweredRadiationSourceComponent> ent, ref PowerChangedEvent args)
    {
        _radiation.SetSourceEnabled(ent.Owner, args.Powered);
    }
}
