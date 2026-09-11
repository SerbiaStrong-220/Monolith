// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class RecoilWeaknessSystem : EntitySystem
{
    [Dependency] private SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RecoilWeaknessComponent, GunShotUserEvent>(OnShooterImpulse);
    }

    private void OnShooterImpulse(Entity<RecoilWeaknessComponent> ent, ref GunShotUserEvent args)
    {
        // only a two-handed weapon actually held in both hands trigger this
        if (!TryComp<WieldableComponent>(args.Gun, out var wieldable)
            || !wieldable.Wielded)
            return;

        _stun.TryKnockdown(ent.Owner, ent.Comp.KnockdownTime, true);
    }
}
