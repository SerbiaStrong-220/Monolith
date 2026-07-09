using Content.Server._Exodus.Nebula.Components;
using Content.Server.Radio;

namespace Content.Server._Exodus.Nebula.Hazards;

public sealed partial class NebulaRadioBlackoutSystem : EntitySystem
{
    private const int MaxParentChecks = 8;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent args)
    {
        if (IsRadioBlackoutBlocked(args.RadioSource))
            args.Cancelled = true;
    }

    private void OnRadioReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (IsRadioBlackoutBlocked(args.RadioSource) || IsRadioBlackoutBlocked(args.RadioReceiver))
            args.Cancelled = true;
    }

    private bool IsRadioBlackoutBlocked(EntityUid uid)
    {
        if (Deleted(uid))
            return false;

        if (HasComp<NebulaRadioBlackoutComponent>(uid))
            return true;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        if (IsGridRadioBlackoutBlocked(xform))
            return true;

        var parent = xform.ParentUid;
        for (var i = 0; i < MaxParentChecks && parent.Valid && parent != uid; i++)
        {
            if (HasComp<NebulaRadioBlackoutComponent>(parent))
                return true;

            if (!TryComp(parent, out TransformComponent? parentXform))
                return false;

            if (IsGridRadioBlackoutBlocked(parentXform))
                return true;

            var nextParent = parentXform.ParentUid;
            if (nextParent == parent)
                return false;

            parent = nextParent;
        }

        return false;
    }

    private bool IsGridRadioBlackoutBlocked(TransformComponent xform)
    {
        return xform.GridUid is { Valid: true } grid &&
               HasComp<NebulaRadioBlackoutComponent>(grid);
    }
}
