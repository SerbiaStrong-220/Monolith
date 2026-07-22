namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures how much nebula thrust reduction this thruster ignores.
/// Thrusters without this component use the full nebula reduction.
/// Values above 1 are allowed and turn slowdown from slowing nebulas into extra thrust.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaThrustResistanceComponent : Component
{
    /// <summary>
    /// 0 means full nebula reduction applies, 1 means the thruster fully ignores it.
    /// Values above 1 overcompensate slowing nebulas and boost the thruster there.
    /// Example: nebula multiplier 0.5 and resistance 2 produce effective multiplier 1.5.
    /// If this field is omitted on a prototype with this component, the thruster fully ignores it.
    /// </summary>
    [DataField]
    public float Resistance = 1f;
}
