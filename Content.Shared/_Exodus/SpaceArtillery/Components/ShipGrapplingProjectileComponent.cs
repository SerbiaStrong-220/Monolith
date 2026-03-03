using Robust.Shared.GameStates;

namespace Content.Shared.Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingProjectileComponent : Component
{
    [DataField, AutoNetworkedField]
    public NetEntity Gun;
}
