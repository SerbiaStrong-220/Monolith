using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Actions;

/// <summary>A cooldown displayed independently of action availability, e.g. when a form can always be exited.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ActionCooldownDisplayComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField] public TimeSpan Start;
    [DataField, AutoNetworkedField, AutoPausedField] public TimeSpan End;
}
