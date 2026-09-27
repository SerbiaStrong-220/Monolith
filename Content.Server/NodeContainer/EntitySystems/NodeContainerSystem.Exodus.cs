using Content.Server._Exodus.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server.NodeContainer.EntitySystems;

/// <summary>
/// Exodus: indexes offset pipe ports during the existing node lifecycle so either construction order works.
/// </summary>
public sealed partial class NodeContainerSystem
{
    [Dependency] private readonly SharedMapSystem _offsetPipeMaps = default!;

    private void UpdateOffsetPipePorts(Entity<NodeContainerComponent> ent)
    {
        var transform = Transform(ent);
        foreach (var node in ent.Comp.Nodes.Values)
        {
            if (node is not PipeNode pipe || pipe.ConnectionOffset == Vector2i.Zero && pipe.OffsetGrid == null)
                continue;

            if (pipe.Deleting || pipe.ConnectionOffset == Vector2i.Zero || !transform.Anchored ||
                !TryComp<MapGridComponent>(transform.GridUid, out var grid))
            {
                RemoveOffsetPipePort(pipe);
                continue;
            }

            var tile = pipe.GetConnectionTile((ent, transform), (transform.GridUid.Value, grid), _offsetPipeMaps);
            if (pipe.OffsetGrid == transform.GridUid && pipe.OffsetTile == tile)
                continue;

            RemoveOffsetPipePort(pipe);
            var index = EnsureComp<OffsetPipeNodeGridComponent>(transform.GridUid.Value);
            if (!index.Ports.TryGetValue(tile, out var ports))
            {
                ports = new HashSet<PipeNode>();
                index.Ports.Add(tile, ports);
            }

            ports.Add(pipe);
            pipe.OffsetGrid = transform.GridUid;
            pipe.OffsetTile = tile;
            _nodeGroupSystem.QueueReflood(pipe);
        }
    }

    private void RemoveOffsetPipePort(PipeNode pipe)
    {
        if (pipe.OffsetGrid is not { } grid)
            return;

        if (TryComp<OffsetPipeNodeGridComponent>(grid, out var index) &&
            index.Ports.TryGetValue(pipe.OffsetTile, out var ports))
        {
            ports.Remove(pipe);
            if (ports.Count == 0)
                index.Ports.Remove(pipe.OffsetTile);
        }

        pipe.OffsetGrid = null;
    }
}
