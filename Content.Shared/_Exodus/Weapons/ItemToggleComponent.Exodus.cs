namespace Content.Shared.Item.ItemToggle.Components;

public sealed partial class ItemToggleComponent
{
    /// <summary>Activate on wield and deactivate on unwield. Disable for independent power and grip controls.</summary>
    [DataField]
    public bool ToggleOnWield = true;
}
