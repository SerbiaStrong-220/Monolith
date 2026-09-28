using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._Exodus.Virology.Intelligent;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Virology.Intelligent;

[UsedImplicitly]
public sealed partial class RotIntelligentBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    private RotIntelligentWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RotIntelligentWindow>();
        if (EntMan.TryGetComponent<RotIntelligentComponent>(Owner, out var core))
        {
            _window.Configure(core, _prototypes);
            _window.RefreshSelection(core, _prototypes);
        }
        _window.Message += SendMessage;
        _window.Selected += id =>
        {
            if (!EntMan.TryGetComponent<RotIntelligentComponent>(Owner, out var colony))
                return;
            SendMessage(new RotSelectBuildingMessage(id, colony.Rotation));
            if (colony.BuildAction is { } action)
                _ui.GetUIController<ActionUIController>().TrySelectTargetAction(action);
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is RotIntelligentUiState colony)
            _window?.UpdateState(colony);
        if (EntMan.TryGetComponent<RotIntelligentComponent>(Owner, out var core))
            _window?.RefreshSelection(core, _prototypes);
    }
}
