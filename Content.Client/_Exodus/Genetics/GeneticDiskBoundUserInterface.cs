using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticDiskBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticDiskWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticDiskWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticDiskUiState data)
            _window?.UpdateState(data);
    }
}
