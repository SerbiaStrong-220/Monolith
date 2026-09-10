namespace Content.Server._Exodus.MapMigration;

/// <summary>
/// Prevents map migration and the aligndoors command from changing this door's rotation.
/// </summary>
[RegisterComponent]
public sealed partial class DoorAutoRotateDisabledComponent : Component
{
}
