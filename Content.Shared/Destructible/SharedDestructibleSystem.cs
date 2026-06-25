namespace Content.Shared.Destructible;

public abstract class SharedDestructibleSystem : EntitySystem
{
    /// <summary>
    ///     Force entity to be destroyed and deleted.
    /// </summary>
    public void DestroyEntity(EntityUid owner, EntityUid? cause = null) // Exodus: add information about causer of destruction
    {
        var eventArgs = new DestructionEventArgs() { Cause = cause }; // Exodus

        RaiseLocalEvent(owner, eventArgs);
        QueueDel(owner);
    }

    /// <summary>
    ///     Force entity to break.
    /// </summary>
    public void BreakEntity(EntityUid owner)
    {
        var eventArgs = new BreakageEventArgs();
        RaiseLocalEvent(owner, eventArgs);
    }
}

/// <summary>
///     Raised when entity is destroyed and about to be deleted.
/// </summary>
public sealed class DestructionEventArgs : EntityEventArgs
{
    public EntityUid? Cause; // Exodus
}

/// <summary>
///     Raised when entity was heavy damage and about to break.
/// </summary>
public sealed class BreakageEventArgs : EntityEventArgs
{

}
