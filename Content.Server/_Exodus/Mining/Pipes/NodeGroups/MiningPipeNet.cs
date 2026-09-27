using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server._Exodus.Mining.Pipes.Nodes;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using JetBrains.Annotations;

namespace Content.Server._Exodus.Mining.Pipes.NodeGroups;

/// <summary>
/// Connectivity only. Liquid metal stays in machine buffers, so rebuilding or splitting a network
/// cannot duplicate, round away, or orphan its contents.
/// </summary>
[NodeGroup(NodeGroupID.ExodusMiningPipe), UsedImplicitly]
public sealed class MiningPipeNet : BaseNodeGroup
{
    public bool HasDevices { get; private set; }

    public override void LoadNodes(List<Node> groupNodes)
    {
        base.LoadNodes(groupNodes);
        foreach (var node in groupNodes)
        {
            if (node is not MiningPipeDeviceNode)
                continue;

            HasDevices = true;
            break;
        }
    }
}
