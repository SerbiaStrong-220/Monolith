namespace Content.Server._Exodus.Nebula.Admin;

/// <summary>
/// Temporary debug marker for Exodus nebula visualization.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaDebugVisualComponent : Component
{
    [ViewVariables]
    public int NebulaIndex = -1;

    [ViewVariables]
    public string Kind = string.Empty;
}
