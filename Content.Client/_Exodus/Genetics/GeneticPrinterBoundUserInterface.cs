using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticPrinterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticPrinterWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticPrinterWindow>();
        _window.OperationRequested += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticPrinterUiState data)
            _window?.UpdateState(data);
    }
}
