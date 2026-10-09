using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Replaces a grid's default bluespace map marker with a hollow regular polygon.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BluespaceMapIconComponent : Component
{
    /// <summary>
    /// Number of polygon sides. The renderer supports between 3 and 32 sides.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Sides = 3;

    /// <summary>
    /// Radius of the hole relative to the outer polygon, clamped to 0.05–0.95 when drawn.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float InnerRadius = 0.6f;
}
