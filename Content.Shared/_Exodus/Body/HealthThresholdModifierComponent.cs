using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Body;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(HealthThresholdModifierSystem))]
public sealed partial class HealthThresholdModifierComponent : Component
{
    /// <summary>
    /// Multiplier applied to all mob-state health thresholds while this component is present.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Multiplier = 1f;
}
