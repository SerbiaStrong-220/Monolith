using Content.Client._Exodus.Shuttles.UI;
using Content.Shared._Exodus.Shuttles;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Shuttles.BUI;

[UsedImplicitly]
public sealed class ReadOnlyBluespaceMapBoundUserInterface : BoundUserInterface
{
    private ReadOnlyBluespaceMapWindow? _window;

    public ReadOnlyBluespaceMapBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ReadOnlyBluespaceMapWindow>();
        _window.Setup(Owner);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
