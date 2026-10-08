using Content.Shared._Exodus.BoxSorter;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.BoxSorter;

[UsedImplicitly]
public sealed class CargoBoxTeleporterBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private CargoBoxTeleporterWindow? _window;

    public CargoBoxTeleporterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<CargoBoxTeleporterWindow>();
        _window.OnChannelSelected += channel =>
        {
            SendMessage(new CargoBoxTeleporterSetChannelMessage(channel));
            Close();
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CargoBoxTeleporterUiState padState)
            return;

        _window?.UpdateState(padState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window = null;
    }
}
