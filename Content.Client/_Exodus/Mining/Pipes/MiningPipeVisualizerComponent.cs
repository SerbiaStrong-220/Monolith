namespace Content.Client._Exodus.Mining.Pipes;

[RegisterComponent]
public sealed partial class MiningPipeVisualizerComponent : Component
{
    [DataField]
    public string IdlePrefix = "idle_";

    [DataField]
    public string FlowPrefix = "flow_";
}
