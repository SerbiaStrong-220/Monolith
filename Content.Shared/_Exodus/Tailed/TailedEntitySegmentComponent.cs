// (c) Space Exodus Team - MPL-2.0 with CLA
// Authors: Lokilife
using Robust.Shared.GameStates;

namespace Content.Shared.Exodus.Tailed;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TailedEntitySegmentComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid HeadEntity;

    [DataField, AutoNetworkedField]
    public int Index;
}
