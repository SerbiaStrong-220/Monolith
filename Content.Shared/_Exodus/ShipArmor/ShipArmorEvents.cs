// (c) Space Exodus Team
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._Exodus.ShipArmor;

/// <summary>
/// Raised on the armor block after charge changes (absorb or regen).
/// </summary>
[ByRefEvent]
public record struct ShipArmorChargeChangedEvent(
    FixedPoint2 OldCharge,
    FixedPoint2 NewCharge,
    FixedPoint2 MaxCharge);

/// <summary>
/// Raised on the armor block when charge hits zero after absorbing damage.
/// </summary>
[ByRefEvent]
public record struct ShipArmorDepletedEvent;

/// <summary>
/// Raised before a grid tile is damaged by an explosion or shuttle impact.
/// </summary>
[ByRefEvent]
public record struct ShipArmorTileDamageEvent(
    EntityUid Grid,
    Vector2i Tile,
    FixedPoint2 Damage,
    bool Cancelled = false);
