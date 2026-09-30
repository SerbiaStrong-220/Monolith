using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Stealth.Components;

/// <summary>Temporarily overrides every cloak source without discarding passive source registrations.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class StealthSuppressedComponent : Component
{
    /// <summary>Server time at which the suppression expires.</summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}
