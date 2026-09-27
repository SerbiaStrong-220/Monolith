using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.MedicalTracking;

public sealed class MedicalTrackingBoundUserInterface : BoundUserInterface
{
    private MedicalTrackingWindow? _window;

    public MedicalTrackingBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MedicalTrackingWindow>();
        _window.Setup(Owner);
        if (State is MedicalTrackingState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MedicalTrackingState tracking)
            _window?.UpdateState(tracking);
    }
}
