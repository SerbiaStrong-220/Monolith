using Content.Shared.Exodus.Stealth.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Exodus.Stealth;

namespace Content.Server.Exodus.Stealth;

public sealed class StealthOnHeldSystem : EntitySystem
{
    [Dependency] private readonly SharedStealthSystem _stealthSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StealthOnHeldComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<StealthOnHeldComponent, GotUnequippedHandEvent>(OnUnequipped);
    }

    private void OnEquipped(EntityUid uid, StealthOnHeldComponent comp, GotEquippedHandEvent args)
    {
        if (args.Handled)
            return;

        _stealthSystem.RequestStealth(args.User, nameof(StealthOnHeldSystem), comp.Stealth);
    }

    private void OnUnequipped(EntityUid uid, StealthOnHeldComponent comp, GotUnequippedHandEvent args)
    {
        if (args.Handled)
            return;

        _stealthSystem.RemoveRequest(nameof(StealthOnHeldSystem), args.User);
    }
}
