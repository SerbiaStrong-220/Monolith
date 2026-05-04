using Robust.Shared.GameStates;

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
    public Color FillColor = new Color(1f, 0.88f, 0f, 0.12f);

    [DataField]
    public Color BorderColor = new Color(1f, 0.88f, 0f, 0.50f);
}
