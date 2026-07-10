namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Visual configuration for the nebula's radar blip.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaRadarVisualsComponent : Component
{
    [DataField(required: true)]
    public Color RadarColor;
}
