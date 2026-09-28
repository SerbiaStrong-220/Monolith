namespace Content.Server._Exodus.Body;

/// <summary>Allows an environment-specific defense without suppressing weapon damage of the same type.</summary>
[ByRefEvent]
public record struct EnvironmentDamageAttemptEvent(EnvironmentHazard Hazard, bool Cancelled = false);

public enum EnvironmentHazard : byte
{
    LowPressure,
    Cold,
}
