namespace Content.Shared._Exodus.Stealth;

/// <summary>Allows disguises without a stealth layer to opt into forced reveal and suppression.</summary>
[ByRefEvent]
public record struct StealthRevealAttemptEvent(bool CanReveal = false);
