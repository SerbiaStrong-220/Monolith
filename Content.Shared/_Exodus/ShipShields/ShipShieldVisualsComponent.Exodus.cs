using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

public sealed partial class ShipShieldVisualsComponent
{
    [DataField, AutoNetworkedField]
    public int LayerCount = 1;

    [DataField, AutoNetworkedField]
    public float LayerThickness = 1.3f;

    [DataField, AutoNetworkedField]
    public float LayerGap;
}
