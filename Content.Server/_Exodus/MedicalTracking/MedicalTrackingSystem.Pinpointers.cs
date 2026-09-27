using Content.Server.Pinpointer;
using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.Pinpointer;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    [Dependency] private PinpointerSystem _pinpointer = default!;

    private void InitializePinpointers()
    {
        Subs.BuiEvents<MedicalTrackingPinpointerComponent>(MedicalTrackingUiKey.Pinpointer, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnPinpointerOpened);
            subs.Event<MedicalTrackingSelectMessage>(OnSelect);
        });
    }

    private void OnPinpointerOpened(Entity<MedicalTrackingPinpointerComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_access.IsAllowed(args.Actor, ent))
        {
            _ui.CloseUi(ent.Owner, MedicalTrackingUiKey.Pinpointer, args.Actor);
            return;
        }

        SendPinpointerState(ent, GetBrains());
    }

    private void OnSelect(Entity<MedicalTrackingPinpointerComponent> ent, ref MedicalTrackingSelectMessage args)
    {
        if (!_ui.IsUiOpen(ent.Owner, MedicalTrackingUiKey.Pinpointer, args.Actor) || !_access.IsAllowed(args.Actor, ent))
            return;

        EntityUid? target = null;
        if (args.Target is { } netTarget)
        {
            if (!TryGetEntity(netTarget, out var resolved) || resolved is not { } brain ||
                !_brainQuery.TryGetComponent(brain, out var registration) || !CanTrackBrain((brain, registration)))
                return;

            target = brain;
        }

        if (TryComp<PinpointerComponent>(ent, out var pointer))
        {
            if (pointer.IsActive != (target != null))
                _pinpointer.TogglePinpointer(ent, pointer);
            _pinpointer.SetTarget(ent, target, pointer);
        }

        SendPinpointerState(ent, GetBrains());
    }

    private void UpdatePinpointers(TimeSpan now)
    {
        List<MedicalTrackingBrain>? brains = null;
        var query = EntityQueryEnumerator<MedicalTrackingPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var device, out var pointer))
        {
            if (now < device.NextUpdate)
                continue;

            var interval = device.UpdateInterval > TimeSpan.Zero ? device.UpdateInterval : TimeSpan.FromSeconds(1);
            device.NextUpdate += interval * (1 + (now - device.NextUpdate).Ticks / interval.Ticks);

            if (pointer.Target is { } target &&
                (!_brainQuery.TryGetComponent(target, out var registration) || !CanTrackBrain((target, registration))))
            {
                _pinpointer.SetTarget(uid, null, pointer);
                if (pointer.IsActive)
                    _pinpointer.TogglePinpointer(uid, pointer);
            }

            if (!_ui.IsUiOpen(uid, MedicalTrackingUiKey.Pinpointer))
                continue;

            brains ??= GetBrains();
            SendPinpointerState((uid, device), brains);
        }
    }

    internal List<MedicalTrackingBrain> GetBrains()
    {
        var brains = new List<MedicalTrackingBrain>();
        var query = EntityQueryEnumerator<MedicalTrackingBrainComponent>();
        while (query.MoveNext(out var uid, out var brain))
        {
            if (CanTrackBrain((uid, brain)))
                brains.Add(new MedicalTrackingBrain(GetNetEntity(uid), brain.ClientName, brain.TierName, brain.TierColor));
        }

        return brains;
    }

    private void SendPinpointerState(Entity<MedicalTrackingPinpointerComponent> ent, List<MedicalTrackingBrain> brains)
    {
        NetEntity? target = null;
        if (TryComp<PinpointerComponent>(ent, out var pointer) && pointer.Target is { } uid &&
            _brainQuery.TryGetComponent(uid, out var brain) && CanTrackBrain((uid, brain)))
            target = GetNetEntity(uid);

        _ui.SetUiState(ent.Owner, MedicalTrackingUiKey.Pinpointer,
            new MedicalTrackingPinpointerState(brains, target));
    }
}
