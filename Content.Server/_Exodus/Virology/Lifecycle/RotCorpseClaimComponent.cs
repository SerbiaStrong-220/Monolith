namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Prevents two scavengers from consuming the same corpse at once.</summary>
[RegisterComponent]
public sealed partial class RotCorpseClaimComponent : Component
{
    [DataField]
    public EntityUid Consumer;
}
