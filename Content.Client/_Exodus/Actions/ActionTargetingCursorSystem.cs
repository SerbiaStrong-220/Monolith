using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._Exodus.Actions;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Exodus.Actions;

/// <summary>
/// Shows the targeting cursor only for opted-in actions, independently of the held-item overlay.
/// </summary>
public sealed class ActionTargetingCursorSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private EntityQuery<ActionTargetingCursorComponent> _targetingCursors;

    public override void Initialize()
    {
        base.Initialize();
        _targetingCursors = GetEntityQuery<ActionTargetingCursorComponent>();
    }

    /// <summary>
    /// Whether the cursor is over a viewport while an action with the opt-in marker is selected.
    /// Menu controls retain their normal cursor behavior.
    /// </summary>
    public bool IsTargetingViewport()
    {
        if ((_ui.ControlFocused ?? _ui.CurrentlyHovered) is not IViewportControl)
            return false;

        var controller = _ui.GetUIController<ActionUIController>();
        return controller.SelectingTargetFor is { } actionId && _targetingCursors.HasComp(actionId);
    }
}
