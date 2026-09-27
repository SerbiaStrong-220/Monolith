using Content.Shared._Exodus.Medical;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Server._Exodus.Medical;

public sealed partial class MedicalBeamGunSystem
{
    private string GetModeName(MedicalBeamMode mode)
    {
        return Loc.GetString(mode == MedicalBeamMode.Automatic
            ? "medical-beam-gun-mode-automatic"
            : "medical-beam-gun-mode-manual");
    }

    private static MedicalBeamMode NextMode(MedicalBeamMode mode)
    {
        return mode == MedicalBeamMode.Manual ? MedicalBeamMode.Automatic : MedicalBeamMode.Manual;
    }

    private void OnUseInHand(Entity<MedicalBeamGunComponent> ent, ref UseInHandEvent args)
    {
        if (!args.Handled)
            args.Handled = TrySetMode(ent, args.User, NextMode(ent.Comp.Mode));
    }

    private void OnModeVerb(Entity<MedicalBeamGunComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !_handsQuery.TryComp(args.User, out var hands) || !_hands.IsHolding((args.User, hands), ent.Owner))
            return;

        var user = args.User;
        var next = NextMode(ent.Comp.Mode);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("medical-beam-gun-selector-verb", ("mode", GetModeName(next))),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
            Act = () => TrySetMode(ent, user, next),
        });
    }

    internal bool TrySetMode(Entity<MedicalBeamGunComponent> ent, EntityUid user, MedicalBeamMode mode)
    {
        if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(user) ||
            mode is not (MedicalBeamMode.Manual or MedicalBeamMode.Automatic) ||
            !_handsQuery.TryComp(user, out var hands) || !_hands.IsHolding((user, hands), ent.Owner) ||
            !_blocker.CanInteract(user, ent) || !_blocker.CanUseHeldEntity(user, ent))
            return false;
        if (ent.Comp.Mode == mode)
            return true;

        if (_activeQuery.TryComp(ent, out var active))
            StopHealing((ent, active));
        ent.Comp.Mode = mode;
        Dirty(ent);
        _audio.PlayPvs(ent.Comp.ModeSound, ent);
        _popup.PopupEntity(Loc.GetString("medical-beam-gun-mode-popup", ("mode", GetModeName(mode))), ent, user);
        return true;
    }
}
