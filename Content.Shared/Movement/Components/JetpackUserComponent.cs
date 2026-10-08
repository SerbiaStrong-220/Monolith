using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Added to someone with an enabled jetpack, including while it is waiting for weightlessness. // Exodus
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JetpackUserComponent : Component
{
    // Exodus-begin: enabling a jetpack is separate from actually flying.
    /// <summary>
    /// Whether the selected jetpack is currently providing flight and movement modifiers.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Active;
    // Exodus-end

    [DataField, AutoNetworkedField]
    public EntityUid Jetpack;

    [DataField, AutoNetworkedField]
    public float WeightlessAcceleration;

    [DataField, AutoNetworkedField]
    public float WeightlessFriction;

    [DataField, AutoNetworkedField]
    public float WeightlessFrictionNoInput;

    [DataField, AutoNetworkedField]
    public float WeightlessModifier;
}
