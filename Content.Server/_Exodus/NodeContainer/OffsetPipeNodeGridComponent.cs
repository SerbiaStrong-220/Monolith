using Content.Server.NodeContainer.Nodes;

namespace Content.Server._Exodus.NodeContainer;

/// <summary>Spatial index of pipe ports whose tile differs from their owner's anchor tile.</summary>
[RegisterComponent]
public sealed partial class OffsetPipeNodeGridComponent : Component
{
    public readonly Dictionary<Vector2i, HashSet<PipeNode>> Ports = new();
}
