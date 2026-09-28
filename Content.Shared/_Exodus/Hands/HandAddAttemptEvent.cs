namespace Content.Shared._Exodus.Hands;

/// <summary>Raised before creating a functional hand, including surgery and transplantation.</summary>
[ByRefEvent]
public record struct HandAddAttemptEvent
{
    public bool Cancelled;
}
