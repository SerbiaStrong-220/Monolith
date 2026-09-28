using Content.Shared.Atmos;

namespace Content.Server._Exodus.Body;

/// <summary>Allows an organ to replace gas exchange, without bypassing suffocation.</summary>
[ByRefEvent]
public record struct RespirationAttemptEvent
{
    public bool Handled;
}

[ByRefEvent]
public record struct CanBreatheGasEvent(GasMixture Gas)
{
    public bool? Result;
}
