namespace Content.Shared._Exodus.ShipShields;

/// <summary>
/// Identifies what caused a shield failure attempt.
/// </summary>
public enum ShipShieldOverloadCause
{
    /// <summary>
    /// Shield damage reached its overload threshold.
    /// </summary>
    Damage,

    /// <summary>
    /// The emitter lost its external power supply.
    /// </summary>
    PowerLoss,
}

/// <summary>
/// Raised before a ship shield fails from damage or reports a genuine loss of external power.
/// For damage overloads, the power state is sampled before the new load is applied to the network.
/// A subscriber that resolves a damage overload can set <see cref="Cancelled"/>; cancellation does
/// not prevent a real power loss from dropping the shield.
/// </summary>
[ByRefEvent]
public record struct ShipShieldOverloadAttemptEvent(
    ShipShieldOverloadCause Cause,
    bool PoweredBeforeLoad)
{
    public bool Cancelled;
}
