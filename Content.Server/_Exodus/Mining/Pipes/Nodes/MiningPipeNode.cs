using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.Pipes.Nodes;

/// <summary>
/// Subfloor ore duct node. Connects to neighbors and to <see cref="MiningPipeDeviceNode"/> on the same tile.
/// </summary>
[DataDefinition]
public sealed partial class MiningPipeNode : Node
{
    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var gridIndex = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        foreach (var (dir, node) in NodeHelpers.GetCardinalNeighborNodes(nodeQuery, gridEnt, gridIndex, mapSystem))
        {
            if (node is MiningPipeNode && node != this)
                yield return node;

            if (node is MiningPipeDeviceNode && dir == Direction.Invalid)
                yield return node;
        }
    }
}
