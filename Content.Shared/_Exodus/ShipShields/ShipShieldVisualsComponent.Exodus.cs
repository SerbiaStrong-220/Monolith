using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

public sealed partial class ShipShieldVisualsComponent
{
    /// <summary>
    /// Width in metres of the ripple band inside the collision boundary. Zero disables the effect.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RippleWidth;

    /// <summary>
    /// Number of visual waves passing a point each second; animation runs entirely on the client.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float RippleSpeed = 0.5f;

    /// <summary>
    /// Client shader used by the animated contour. ShaderPrototype is unavailable in shared code.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string RippleShader = "ShipShieldRipple";

    [DataField, AutoNetworkedField]
    public int LayerCount = 1;

    [DataField, AutoNetworkedField]
    public float LayerThickness = 1.3f;

    [DataField, AutoNetworkedField]
    public float LayerGap;
}
