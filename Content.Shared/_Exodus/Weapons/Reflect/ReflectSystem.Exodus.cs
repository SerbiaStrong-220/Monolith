using Content.Shared._Exodus.Weapons.Reflect;
using Content.Shared.Hands.Components;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable.Components;

namespace Content.Shared.Weapons.Reflect;

public sealed partial class ReflectSystem
{
    [Dependency] private EntityWhitelistSystem _reflectWhitelist = default!;

    private bool CanReflect(EntityUid user, Entity<ReflectComponent> reflector, EntityUid? shot)
    {
        if (!_toggle.IsActivated((reflector.Owner, null)))
            return false;

        if (reflector.Comp.RequiresHeld && !_handsSystem.IsHolding((user, null), reflector.Owner))
            return false;

        if (reflector.Comp.RequiresWield &&
            (!TryComp<WieldableComponent>(reflector, out var wieldable) || !wieldable.Wielded))
            return false;

        return shot == null || !_reflectWhitelist.IsBlacklistPass(reflector.Comp.ProjectileBlacklist, shot.Value);
    }

    private Entity<ReflectComponent>? FindBestReflector(EntityUid user, ReflectType type, EntityUid? shot)
    {
        Entity<ReflectComponent>? best = null;
        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity is { } item)
                    ConsiderReflector(user, item, type, shot, ref best);
            }
        }

        if (_inventorySystem.TryGetSlotEntity(user, "outerClothing", out var outer) && outer is { } outerItem)
            ConsiderReflector(user, outerItem, type, shot, ref best);

        if (_inventorySystem.TryGetSlotEntity(user, "vest", out var vest) && vest is { } vestItem)
            ConsiderReflector(user, vestItem, type, shot, ref best);

        return best;
    }

    private void ConsiderReflector(EntityUid user, EntityUid candidate, ReflectType type, EntityUid? shot,
        ref Entity<ReflectComponent>? best)
    {
        if (!TryComp<ReflectComponent>(candidate, out var reflect) ||
            (reflect.Reflects & type) == 0 ||
            (best is { } previous && previous.Comp.ReflectProb >= reflect.ReflectProb) ||
            !CanReflect(user, (candidate, reflect), shot))
            return;

        best = (candidate, reflect);
    }

    private void NotifyShotReflected(EntityUid user, EntityUid reflector, EntityUid? shot, EntityUid? shooter)
    {
        if (!_netManager.IsServer || shot is not { } source || Deleted(source))
            return;

        if (!TryComp<ReflectedShotComponent>(source, out var reflected))
        {
            reflected = AddComp<ReflectedShotComponent>(source);
            reflected.OriginalShooter = shooter;
        }

        if (Deleted(reflector))
            return;

        var ev = new ShotReflectedEvent(user, source, reflected.OriginalShooter);
        RaiseLocalEvent(reflector, ref ev);
    }
}
