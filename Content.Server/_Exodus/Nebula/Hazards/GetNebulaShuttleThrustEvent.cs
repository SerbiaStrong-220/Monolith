namespace Content.Server._Exodus.Nebula.Hazards;

[ByRefEvent]
public record struct GetNebulaShuttleThrustEvent(
    EntityUid ShuttleUid,
    int HorizontalDirectionIndex,
    int VerticalDirectionIndex,
    float HorizontalThrust,
    float VerticalThrust);
