using Content.Shared._Exodus.Genetics;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticViewBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticViewWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticViewWindow>();
        _window.TargetSelected += target => SendMessage(new GeneticViewMessage(target));
        _window.RefreshRequested += () => SendMessage(new GeneticViewMessage(null, true));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticViewState data)
            _window?.UpdateState(data);
    }
}
