using Content.Shared._Mono.Radar;
using Robust.Shared.Localization;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Makes an entity appear as an IFF-style marker on the bluespace navigation map.
/// </summary>
[RegisterComponent]
public sealed partial class BluespaceMapBlipComponent : Component
{
    /// <summary>
    /// Shape used to represent the entity on the bluespace navigation map.
    /// </summary>
    [DataField]
    public RadarBlipShape Shape = RadarBlipShape.Diamond;

    /// <summary>
    /// Color of the marker and its optional label.
    /// </summary>
    [DataField]
    public Color Color = Color.OrangeRed;

    /// <summary>
    /// Relative marker size on the bluespace navigation map.
    /// </summary>
    [DataField]
    public float Scale = 1f;

    /// <summary>
    /// Optional localization key displayed next to the marker.
    /// </summary>
    [DataField]
    public LocId? Label;
}
