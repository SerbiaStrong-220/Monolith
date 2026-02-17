namespace Content.Shared.Exodus.SpaceArtillery.Components;

[RegisterComponent]
public sealed partial class ShipGrapplingGunTargetComponent : Component
{
    [DataField]
    public EntityUid Gun;
}
