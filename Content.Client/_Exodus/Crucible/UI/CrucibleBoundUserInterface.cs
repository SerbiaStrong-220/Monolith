using Content.Shared.Crucible;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.Crucible.UI;

public sealed class CrucibleBoundUserInterface : BoundUserInterface
{
    private CrucibleWindow? _window;

    public CrucibleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = new CrucibleWindow();
        _window.OnClose += Close;
        _window.OnStartCookPressed += () => SendMessage(new CrucibleStartCookMessage());
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not CrucibleConsoleState cast) return;

        _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        _window?.Dispose();
        _window = null;
    }
}
