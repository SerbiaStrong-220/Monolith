using Robust.Shared.Maths;

namespace Content.Shared._Exodus.ShipShields;

/// <summary>
/// Configures a ship shield that protects only an arc in front of its generator.
/// </summary>
[RegisterComponent]
public sealed partial class DirectionalShipShieldEmitterComponent : Component
{
    /// <summary>
    /// Width of the protected forward arc in degrees.
    /// </summary>
    [DataField]
    public float ArcDegrees = 180f;
}

/// <summary>
/// Stores the direction and arc captured when a directional shield field is raised.
/// </summary>
[RegisterComponent]
public sealed partial class DirectionalShipShieldFieldComponent : Component
{
    public float ArcDegrees;
    public Angle Direction;
}
