namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Raised on an emitter when its field intercepts an impact or an EMP.
/// </summary>
[ByRefEvent]
public readonly record struct ShipShieldHitEvent;

/// <summary>
/// Allows modifiers to adjust one regeneration step. Active describes the emitter before
/// its overload timer is advanced, so the last overload step cannot receive an active bonus.
/// </summary>
[ByRefEvent]
public record struct ShipShieldRegenerationEvent(float Amount, bool Active);
