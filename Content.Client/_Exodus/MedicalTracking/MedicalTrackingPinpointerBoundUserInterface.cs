using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.MedicalTracking;

public sealed class MedicalTrackingPinpointerBoundUserInterface : BoundUserInterface
{
    private MedicalTrackingPinpointerWindow? _window;

    public MedicalTrackingPinpointerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MedicalTrackingPinpointerWindow>();
        _window.Setup(Owner);
        _window.SelectTarget += target => SendMessage(new MedicalTrackingSelectMessage(target));
        if (State is MedicalTrackingPinpointerState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MedicalTrackingPinpointerState tracking)
            _window?.UpdateState(tracking);
    }
}
