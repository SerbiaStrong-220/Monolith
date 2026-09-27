namespace Content.Shared._Exodus.Weapons.Events;

/// <summary>
/// Recalculates the wield bonus from base values and all currently installed attachments.
/// </summary>
[ByRefEvent]
public record struct GunWieldBonusRefreshEvent(Angle MinAngle, Angle MaxAngle, Angle AngleDecay, Angle AngleIncrease);
