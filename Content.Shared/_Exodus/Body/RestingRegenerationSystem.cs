using Content.Shared._Exodus.Stances;
using Content.Shared._Exodus.Territory;
using Content.Shared.Damage;

namespace Content.Shared._Exodus.Body;

public sealed class RestingRegenerationSystem : EntitySystem
{
    private EntityQuery<PressureBreathingComponent> _breathingQuery;
    private EntityQuery<LocomotionStanceComponent> _stanceQuery;

    public override void Initialize()
    {
        base.Initialize();
        _breathingQuery = GetEntityQuery<PressureBreathingComponent>();
        _stanceQuery = GetEntityQuery<LocomotionStanceComponent>();
        SubscribeLocalEvent<RestingRegenerationComponent, ModifyPassiveDamageEvent>(OnRegenerate);
    }

    private void OnRegenerate(Entity<RestingRegenerationComponent> ent, ref ModifyPassiveDamageEvent args)
    {
        var resting = _stanceQuery.TryComp(ent, out var stance) &&
                      stance.Stance == LocomotionStance.Curled && stance.TransitionEnd == TimeSpan.Zero;
        var breathing = _breathingQuery.TryComp(ent, out var lungs) && lungs.CanBreathe;
        if (!resting && breathing)
            return;

        // Modify only this pulse; never mutate the prototype's shared DamageSpecifier.
        args.Damage = resting ? args.Damage * ent.Comp.RestingMultiplier : new DamageSpecifier(args.Damage);
        if (breathing)
            return;

        foreach (var type in ent.Comp.RequiresBreathing)
            args.Damage.DamageDict.Remove(type.Id);
    }
}
