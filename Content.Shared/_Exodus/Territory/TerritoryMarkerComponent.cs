using Robust.Shared.GameStates;
using Robust.Shared.Localization;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Draws a filled semi-transparent territory circle on the navigation radar.
/// Intended for bases and outposts to mark their sphere of influence.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TerritoryMarkerComponent : Component
{
    [DataField]
    public float Radius = 7500f;

    [DataField]
    public Color FillColor = new Color(0.65f, 0.65f, 0.65f, 0.02f);

    [DataField]
    public Color BorderColor = new Color(0.70f, 0.70f, 0.70f, 0.085f);

    /// <summary>
    /// Localization key for the label repeated diagonally across the territory zone.
    /// </summary>
    [DataField]
    public LocId Text = "territory-marker-default";
}
