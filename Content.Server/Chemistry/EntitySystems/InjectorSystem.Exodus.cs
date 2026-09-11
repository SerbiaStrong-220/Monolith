// SS220 / Exodus: allow anatomy and disease effects to reject needle interactions.
using Content.Shared._Exodus.Virology;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class InjectorSystem
{
    private bool CanUseNeedle(EntityUid target, EntityUid user)
    {
        var ev = new VirusInjectionAttemptEvent();
        RaiseLocalEvent(target, ref ev);
        if (!ev.Cancelled)
            return true;

        if (ev.Message is { } message)
            Popup.PopupEntity(message, target, user);
        return false;
    }
}
