using Content.Shared._Exodus.Weapons.Reflect;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;

namespace Content.Server._Exodus.Weapons.Reflect;

public sealed class ReflectChargeSystem : EntitySystem
{
    [Dependency] private SharedChargesSystem _charges = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ReflectChargeComponent, ShotReflectedEvent>(OnReflected);
    }

    private void OnReflected(Entity<ReflectChargeComponent> ent, ref ShotReflectedEvent args)
    {
        if (args.OriginalShooter == args.User || ent.Comp.ChargesPerReflection <= 0 ||
            !TryComp<ReflectedShotComponent>(args.Shot, out var shot) || shot.ChargeGranted ||
            !TryComp<LimitedChargesComponent>(ent, out var charges))
            return;

        // Mark even at capacity: bouncing the same shot again must not recharge after a hit.
        shot.ChargeGranted = true;
        var room = Math.Max(0, charges.MaxCharges - charges.Charges);
        _charges.AddCharges(ent, Math.Min(room, ent.Comp.ChargesPerReflection), charges);
    }
}
