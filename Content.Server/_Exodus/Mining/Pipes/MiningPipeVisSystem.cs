using Content.Server._Exodus.Mining.Pipes.Components;
using Content.Server._Exodus.Mining.Pipes.Nodes;
using Content.Server._Exodus.Mining.Pipes.NodeGroups;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.NodeContainer;
using Content.Shared.Wires;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.Pipes;

public sealed partial class MiningPipeVisSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningPipeVisComponent, NodeGroupsRebuilt>(UpdateAppearance);
    }

    private void UpdateAppearance(Entity<MiningPipeVisComponent> ent, ref NodeGroupsRebuilt args)
    {
        if (!_nodeContainer.TryGetNode(ent.Owner, ent.Comp.Node, out MiningPipeNode? node))
            return;

        var transform = Transform(ent);
        if (!TryComp<MapGridComponent>(transform.GridUid, out var grid))
            return;

        var mask = WireVisDirFlags.None;
        var tile = _map.TileIndicesFor((transform.GridUid.Value, grid), transform.Coordinates);

        foreach (var reachable in node.ReachableNodes)
        {
            if (reachable is not MiningPipeNode)
                continue;

            var otherTransform = Transform(reachable.Owner);
            var otherTile = _map.TileIndicesFor((transform.GridUid.Value, grid), otherTransform.Coordinates);
            var diff = otherTile - tile;

            mask |= diff switch
            {
                (0, 1) => WireVisDirFlags.North,
                (0, -1) => WireVisDirFlags.South,
                (1, 0) => WireVisDirFlags.East,
                (-1, 0) => WireVisDirFlags.West,
                _ => WireVisDirFlags.None
            };
        }

        _appearance.SetData(ent, WireVisVisuals.ConnectedMask, mask);
        _appearance.SetData(ent, MiningPipeVisuals.Connected,
            node.NodeGroup is MiningPipeNet { HasDevices: true, Removed: false, Remaking: false });
    }
}
