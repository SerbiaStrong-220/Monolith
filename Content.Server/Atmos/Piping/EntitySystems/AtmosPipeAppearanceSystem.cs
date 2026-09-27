using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.Piping.EntitySystems;

public sealed partial class AtmosPipeAppearanceSystem : SharedAtmosPipeAppearanceSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PipeAppearanceComponent, NodeGroupsRebuilt>(OnNodeUpdate);
    }

    private void OnNodeUpdate(EntityUid uid, PipeAppearanceComponent component, ref NodeGroupsRebuilt args)
    {
        UpdateAppearance(args.NodeOwner);
    }

    private void UpdateAppearance(EntityUid uid, AppearanceComponent? appearance = null, NodeContainerComponent? container = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref appearance, ref container, ref xform, false))
            return;

        if (!TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return;

        var numberOfPipeLayers = GetNumberOfPipeLayers(uid, out var atmosPipeLayers);

        // Exodus-begin: connect visuals to pipe ports, including offsets on large machines.
        var connectedDirections = new PipeDirection[numberOfPipeLayers];
        Array.Fill(connectedDirections, PipeDirection.None);

        foreach (var node in container.Nodes.Values)
        {
            if (node is not PipeNode pipe)
                continue;

            var tile = pipe.GetConnectionTile((uid, xform), (xform.GridUid.Value, grid), _map);
            foreach (var connectedNode in pipe.ReachableNodes)
            {
                if (connectedNode is not PipeNode neighbor ||
                    (int)neighbor.CurrentPipeLayer >= numberOfPipeLayers)
                    continue;

                var otherTile = neighbor.GetConnectionTile((neighbor.Owner, Transform(neighbor.Owner)),
                    (xform.GridUid.Value, grid), _map);
                connectedDirections[(int)neighbor.CurrentPipeLayer] |= (otherTile - tile) switch
                {
                    (0, 1) => PipeDirection.North,
                    (0, -1) => PipeDirection.South,
                    (1, 0) => PipeDirection.East,
                    (-1, 0) => PipeDirection.West,
                    _ => PipeDirection.None
                };
            }
        }
        // Exodus-end

        // Convert the pipe direction array into a single int for serialization
        var netConnectedDirections = 0;

        for (var i = numberOfPipeLayers - 1; i >= 0; i--)
            netConnectedDirections += (int)connectedDirections[i] << (PipeDirectionHelpers.PipeDirections * i);

        _appearance.SetData(uid, PipeVisuals.VisualState, netConnectedDirections, appearance);
    }
}
