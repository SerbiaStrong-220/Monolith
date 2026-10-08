using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.BoxSorter;

[RegisterComponent]
public sealed partial class CargoTeleporterFrameComponent : Component
{
    [DataField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(2);
}
