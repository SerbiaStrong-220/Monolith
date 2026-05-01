namespace Content.Server._Exodus.Nebula;

[ByRefEvent]
public record struct GetNebulaShuttleThrustEvent(
    EntityUid ShuttleUid,
    float HorizontalThrust,
    float VerticalThrust);
