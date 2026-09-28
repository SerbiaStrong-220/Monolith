using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Body;
using Content.Shared._Shitmed.Body.Organ;

namespace Content.Server._Exodus.Body;

public sealed partial class PressureBreathingSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private RespiratorSystem _respirator = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PressureBreathingComponent, RespirationAttemptEvent>(OnBreathe);
        SubscribeLocalEvent<PressureBreathingComponent, CanBreatheGasEvent>(OnCanBreatheGas);
    }

    private void OnBreathe(Entity<PressureBreathingComponent> ent, ref RespirationAttemptEvent args)
    {
        if (!TryComp<RespiratorComponent>(ent, out var respirator))
            return;

        args.Handled = true;
        var location = new InhaleLocationEvent();
        RaiseLocalEvent(ent, ref location);
        var gas = location.Gas ?? _atmosphere.GetContainingMixture(ent.Owner);
        var canBreathe = !HasComp<DebrainedComponent>(ent) && gas != null && gas.Pressure > ent.Comp.MinimumPressure;
        if (ent.Comp.CanBreathe != canBreathe)
        {
            ent.Comp.CanBreathe = canBreathe;
            Dirty(ent);
        }

        if (canBreathe)
            _respirator.UpdateSaturation(ent, respirator.MaxSaturation, respirator);
    }

    private void OnCanBreatheGas(Entity<PressureBreathingComponent> ent, ref CanBreatheGasEvent args)
    {
        args.Result = args.Gas.Pressure > ent.Comp.MinimumPressure;
    }
}
