using Content.Shared.FixedPoint;

namespace Content.Shared._Exodus.Radiation;

/// <summary>
/// Raised on the server after an irradiation event deals damage, with the positive damage actually applied
/// after armor and other modifiers. Direct damage, including genetic instability, does not raise this event.
/// </summary>
[ByRefEvent]
public readonly record struct RadiationDamageReceivedEvent(FixedPoint2 Damage);
