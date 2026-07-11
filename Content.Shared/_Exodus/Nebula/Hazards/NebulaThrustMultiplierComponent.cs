namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Directly multiplies this thruster's thrust while its shuttle is inside any nebula.
/// This multiplier is applied before <see cref="NebulaThrustResistanceComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaThrustMultiplierComponent : Component
{
    /// <summary>
    /// 1 means no direct change in nebulas, 0.5 halves thrust, 2 doubles thrust.
    /// Negative values are clamped to 0 by the server system.
    /// </summary>
    [DataField]
    public float Multiplier = 1f;
}
