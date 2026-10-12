using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Utility;

namespace Content.Shared.Materials.OreSilo;

public abstract partial class SharedOreSiloSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    partial void InitializeExodus()
    {
        SubscribeLocalEvent<OreSiloClientComponent, GetVerbsEvent<InteractionVerb>>(OnGetClientInteractionVerbs);
        SubscribeLocalEvent<OreSiloClientComponent, ExaminedEvent>(OnClientExamined);
    }

    private void OnGetClientInteractionVerbs(Entity<OreSiloClientComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || ent.Comp.Silo is not { } silo)
            return;

        if (TerminatingOrDeleted(silo))
        {
            ent.Comp.Silo = null;
            Dirty(ent);
            return;
        }

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("ore-silo-client-unlink-verb-text"),
            Message = Loc.GetString("ore-silo-client-unlink-verb-message", ("silo", Identity.Name(silo, EntityManager))),
            Icon = ent.Comp.DisconnectIcon,
            Act = () => TryDisconnectSilo(ent, user),
            Impact = LogImpact.Low,
        });
    }

    private void OnClientExamined(Entity<OreSiloClientComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Silo is not { } silo)
        {
            args.PushMarkup(Loc.GetString("ore-silo-client-examine-not-connected"));
            return;
        }

        if (TerminatingOrDeleted(silo))
        {
            ent.Comp.Silo = null;
            Dirty(ent);
            args.PushMarkup(Loc.GetString("ore-silo-client-examine-not-connected"));
            return;
        }

        args.PushMarkup(Loc.GetString("ore-silo-client-examine-connected", ("silo", Identity.Name(silo, EntityManager))));
    }

    public bool TryDisconnectSilo(Entity<OreSiloClientComponent> client, EntityUid? user = null)
    {
        if (client.Comp.Silo is not { } siloUid)
            return false;

        var prevSilo = siloUid;
        client.Comp.Silo = null;
        Dirty(client);

        if (TryComp<OreSiloComponent>(prevSilo, out var siloComp))
        {
            siloComp.Clients.Remove(client.Owner);
            Dirty(prevSilo, siloComp);
            UpdateOreSiloUi((prevSilo, siloComp));
        }

        if (client.Comp.DisconnectSound != null)
        {
            if (user != null)
                _audio.PlayPredicted(client.Comp.DisconnectSound, client, user.Value);
            else
                _audio.PlayPvs(client.Comp.DisconnectSound, client);
        }

        if (user != null)
        {
            var msg = Loc.GetString("ore-silo-client-disconnected", ("silo", Identity.Name(prevSilo, EntityManager)));
            _popup.PopupClient(msg, client, user.Value);
        }

        return true;
    }
}
