// Exodus: allow ability menus to select an existing targeted action through the normal action controller.
using Content.Shared.Actions;

namespace Content.Client.UserInterface.Systems.Actions;

public sealed partial class ActionUIController
{
    public bool TrySelectTargetAction(EntityUid actionId)
    {
        if (!_actions.Contains(actionId) || _actionsSystem == null
            || !_actionsSystem.TryGetActionData(actionId, out var action)
            || action is not BaseTargetActionComponent target)
            return false;
        StartTargeting(actionId, target);
        return true;
    }
}
