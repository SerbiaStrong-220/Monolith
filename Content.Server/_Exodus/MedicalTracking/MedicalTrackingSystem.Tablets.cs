using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.Access.Systems;
using Robust.Server.GameObjects;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AccessReaderSystem _access = default!;

    private void InitializeTablets()
    {
        InitializeTabletAudio();
        Subs.BuiEvents<MedicalTrackingTabletComponent>(MedicalTrackingUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnTabletOpened);
            subs.Event<BoundUIClosedEvent>(OnTabletClosed);
        });
    }

    private void OnTabletOpened(Entity<MedicalTrackingTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_access.IsAllowed(args.Actor, ent))
        {
            _ui.CloseUi(ent.Owner, MedicalTrackingUiKey.Key, args.Actor);
            return;
        }

        _ui.SetUiState(ent.Owner, MedicalTrackingUiKey.Key, new MedicalTrackingState(GetContacts()));
        _audio.PlayGlobal(ent.Comp.OpenSound, args.Actor);
    }

    private void UpdateTablets(TimeSpan now)
    {
        MedicalTrackingState? state = null;
        var query = EntityQueryEnumerator<MedicalTrackingTabletComponent>();
        while (query.MoveNext(out var uid, out var tablet))
        {
            UpdateTabletAlert((uid, tablet), now);

            if (now < tablet.NextUpdate)
                continue;

            var interval = tablet.UpdateInterval > TimeSpan.Zero ? tablet.UpdateInterval : TimeSpan.FromSeconds(1);
            tablet.NextUpdate += interval * (1 + (now - tablet.NextUpdate).Ticks / interval.Ticks);

            if (!_ui.IsUiOpen(uid, MedicalTrackingUiKey.Key))
                continue;

            state ??= new MedicalTrackingState(GetContacts());
            _ui.SetUiState(uid, MedicalTrackingUiKey.Key, state);
        }
    }

    internal List<MedicalTrackingContact> GetContacts()
    {
        var contacts = new List<MedicalTrackingContact>();
        var query = EntityQueryEnumerator<MedicalTrackingBodyComponent>();
        while (query.MoveNext(out var uid, out var body))
        {
            if (!TerminatingOrDeleted(uid) && _implantQuery.TryGetComponent(body.Implant, out var implant) &&
                implant.Body == uid && implant.Contact is { } contact && CanTrackBody(uid))
                contacts.Add(contact);
        }

        return contacts;
    }
}
