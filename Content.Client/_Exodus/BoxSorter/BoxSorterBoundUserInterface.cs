using Content.Shared._Exodus.BoxSorter;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.BoxSorter;

[UsedImplicitly]
public sealed class BoxSorterBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private BoxSorterWindow? _window;

    public BoxSorterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<BoxSorterWindow>();
        _window.OnOtherRouteSelected += channel =>
        {
            SendMessage(new BoxSorterSetOtherRouteMessage(channel));
        };
        _window.OnDestinationRouteSelected += (destination, channel) =>
        {
            SendMessage(new BoxSorterSetDestinationRouteMessage(destination, channel));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not BoxSorterUiState sorterState)
            return;

        _window?.UpdateState(sorterState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window = null;
    }
}
