namespace Content.Server._Exodus.Silicons.Borgs;

/// <summary>
///     Tracks which borg module owns this item.
///     Used to check whether the item belongs to a rechargeable module (split abuse prevention).
/// </summary>
[RegisterComponent]
public sealed partial class BorgModuleItemComponent : Component
{
    [DataField]
    public EntityUid ModuleUid;
}
