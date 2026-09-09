namespace Content.Server._Exodus.Ghost.Roles;

/// <summary>
/// Raised before an abandoned ghost role reopens, allowing temporary controllers to reserve it.
/// </summary>
[ByRefEvent]
public record struct GhostRoleReregisterAttemptEvent
{
    public bool Cancelled;
}
