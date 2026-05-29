namespace Content.Shared._Exodus.Communications;

/// <summary>
/// Marks a communications console that cannot change the station alert level: the level selector is
/// hidden and alert-level messages are ignored. Generic — not tied to any faction.
/// </summary>
[RegisterComponent]
public sealed partial class CommsAlertLevelLockComponent : Component;
