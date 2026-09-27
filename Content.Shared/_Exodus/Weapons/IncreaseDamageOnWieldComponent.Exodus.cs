namespace Content.Shared.Wieldable.Components;

public sealed partial class IncreaseDamageOnWieldComponent
{
    /// <summary>Suppress the wield bonus while the item's power toggle is off.</summary>
    [DataField]
    public bool RequiresActivation;
}
