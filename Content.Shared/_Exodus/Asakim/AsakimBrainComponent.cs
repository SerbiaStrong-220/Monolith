namespace Content.Shared._Exodus.Asakim;

/// <summary>
/// Marker placed on the Asakim brain organ entity.
/// Used by <see cref="AsakimIdentitySystem"/> and pinpointer search to avoid
/// scanning every body / tag in the world.
/// </summary>
[RegisterComponent]
public sealed partial class AsakimBrainComponent : Component;
