using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Body;

/// <summary>
/// Respiratory organs that exchange any gas, provided its total pressure is sufficient.
/// Granted by an organ so removing the lungs also removes this ability.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PressureBreathingComponent : Component
{
    [DataField]
    public float MinimumPressure = 20f;

    [DataField, AutoNetworkedField]
    public bool CanBreathe;
}
