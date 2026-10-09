using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent]
public sealed partial class CargoTeleporterFrameComponent : Component
{
    [DataField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId DeployResult = "CargoBoxTeleporter";
}
