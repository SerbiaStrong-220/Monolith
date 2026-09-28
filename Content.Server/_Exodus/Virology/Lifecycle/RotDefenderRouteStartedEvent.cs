namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Lets a behavior release its current task when shared navigation starts an escape or detour.</summary>
[ByRefEvent]
public readonly record struct RotDefenderRouteStartedEvent(RotDefenderRoute Route);
