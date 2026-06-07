namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Configures how much nebula thrust reduction this thruster ignores.
/// Thrusters without this component use the full nebula reduction.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaThrustResistanceComponent : Component
{
    /// <summary>
    /// 0 means full nebula reduction applies, 1 means the thruster fully ignores it.
    /// If this field is omitted on a prototype with this component, the thruster fully ignores it.
    /// </summary>
    [DataField]
    public float Resistance = 1f;
}
