// Exodus: integrate projector disguises with forced reveal and timed suppression.
using Content.Shared._Exodus.Stealth;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.Polymorph.Components;

namespace Content.Shared.Polymorph.Systems;

public abstract partial class SharedChameleonProjectorSystem
{
    [Dependency] private SharedStealthSystem _stealth = default!;

    private void InitializeStealthSuppression()
    {
        SubscribeLocalEvent<ChameleonDisguisedComponent, StealthRevealAttemptEvent>(OnStealthRevealAttempt);
        SubscribeLocalEvent<ChameleonDisguisedComponent, StealthRevealEvent>(OnStealthReveal);
    }

    private void OnStealthRevealAttempt(Entity<ChameleonDisguisedComponent> ent, ref StealthRevealAttemptEvent args)
    {
        args.CanReveal = true;
    }

    private void OnStealthReveal(Entity<ChameleonDisguisedComponent> ent, ref StealthRevealEvent args)
    {
        if (_net.IsClient || TerminatingOrDeleted(ent))
            return;

        TryReveal(ent.AsNullable());
    }

    private bool IsDisguiseSuppressed(EntityUid user)
    {
        if (!_stealth.IsSuppressed(user))
            return false;

        _popup.PopupClient(Loc.GetString("stealth-disruptor-suppressed"), user, user);
        return true;
    }
}
