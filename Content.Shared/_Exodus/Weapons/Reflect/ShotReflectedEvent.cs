namespace Content.Shared._Exodus.Weapons.Reflect;

/// <summary>Raised on the actual reflecting item, on the server, after a successful reflection.</summary>
[ByRefEvent]
public readonly record struct ShotReflectedEvent(EntityUid User, EntityUid Shot, EntityUid? OriginalShooter);
