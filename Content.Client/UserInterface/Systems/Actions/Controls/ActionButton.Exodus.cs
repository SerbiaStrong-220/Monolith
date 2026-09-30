// Exodus: a form's entry cooldown remains visible while the return action is available.
using Content.Shared._Exodus.Actions;

namespace Content.Client.UserInterface.Systems.Actions.Controls;

public sealed partial class ActionButton
{
    private (TimeSpan Start, TimeSpan End)? DisplayedCooldown =>
        _entities.TryGetComponent<ActionCooldownDisplayComponent>(ActionId, out var display)
            ? (display.Start, display.End)
            : _action?.Cooldown;
}
