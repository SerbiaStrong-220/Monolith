using Robust.Shared.Timing;

namespace Content.Shared._Crescent.ShipShields;

public sealed partial class ShipShieldEmitterComponent
{
    /// <summary>
    /// Prevents EMP effects on entities belonging to the protected grid while the shield is operational.
    /// </summary>
    [DataField]
    public bool EmpProtection;

    /// <summary>
    /// Width in metres of the animated ripple band inside the shield boundary. Zero uses the standard visual.
    /// </summary>
    [DataField]
    public float RippleWidth;

    /// <summary>
    /// Number of visual waves passing a point each second.
    /// </summary>
    [DataField]
    public float RippleSpeed = 0.5f;

    /// <summary>
    /// Reduces the load added by projectiles stopped by this shield.
    /// </summary>
    [DataField]
    public float DeflectionDamageModifier = 1f;

    /// <summary>
    /// Server tick in which the current damage overload lockout began.
    /// </summary>
    [ViewVariables]
    public GameTick? DamageOverloadStartedTick;

    /// <summary>
    /// Prevents repeated power-loss notifications until external power returns.
    /// </summary>
    [ViewVariables]
    public bool PowerLossReported;
}
