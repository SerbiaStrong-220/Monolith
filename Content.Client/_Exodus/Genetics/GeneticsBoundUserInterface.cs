using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticsBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticsWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticsWindow>();
        _window.OperationRequested += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticsUiState data)
            _window?.UpdateState(data);
    }
}
