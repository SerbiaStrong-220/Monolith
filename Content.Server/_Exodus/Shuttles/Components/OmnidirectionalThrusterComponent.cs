using Content.Server._Exodus.Shuttles.Systems;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// When present on a thruster, registers it in all 4 linear thrust directions simultaneously.
/// Nozzle direction and space exposure checks are suppressed in examine.
/// </summary>
[RegisterComponent]
[Access(typeof(OmnidirectionalThrusterSystem))]
public sealed partial class OmnidirectionalThrusterComponent : Component
{
    /// <summary>
    /// Grid the thrust was registered on at last enable. Used to remove that same registration
    /// on disable even if the thruster has since moved to a different grid or been unanchored.
    /// </summary>
    [ViewVariables]
    public EntityUid? CurrentGrid;
}
