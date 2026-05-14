namespace Content.Shared._Exodus.ShipShields;

/// <summary>
/// Raised on the shield emitter the moment its accumulated damage first exceeds
/// the configured damage limit and forces an overload. Not raised every tick — only
/// on the rising edge from "below limit" to "above limit".
/// </summary>
[ByRefEvent]
public record struct ShipShieldOverloadedEvent;
