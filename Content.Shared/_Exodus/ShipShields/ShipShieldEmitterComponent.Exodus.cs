using Robust.Shared.Timing;

namespace Content.Shared._Crescent.ShipShields;

public sealed partial class ShipShieldEmitterComponent
{
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
