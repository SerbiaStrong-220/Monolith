using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Mono.Radar;

// Exodus: include active mining beams in the existing, range-filtered radar report.
public sealed partial class RadarBlipSystem
{
    [Dependency] private SharedMapSystem _miningMap = default!; // Exodus: support generated dependency injection.

    private EntityQuery<BulkAutoMiningEmitterComponent> _miningEmitters;
    private EntityQuery<MapGridComponent> _miningGrids;
    private EntityQuery<TransformComponent> _miningTransforms;

    private void InitializeMiningBeams()
    {
        _miningEmitters = GetEntityQuery<BulkAutoMiningEmitterComponent>();
        _miningGrids = GetEntityQuery<MapGridComponent>();
        _miningTransforms = GetEntityQuery<TransformComponent>();
    }

    private BulkAutoMiningRadarBeam? GetMiningBeam(Entity<TransformComponent> ent)
    {
        if (!_miningEmitters.TryComp(ent, out var emitter))
            return null;

        // Consortium links aim at the partner laser's pivot on its own grid.
        if (emitter.LinkPartner is { } partner && emitter.LinkGrid is { } linkGrid)
        {
            if (TerminatingOrDeleted(linkGrid) || !_miningTransforms.TryComp(linkGrid, out var linkXform) ||
                linkXform.MapUid != ent.Comp.MapUid)
                return null;

            return new BulkAutoMiningRadarBeam(GetNetCoordinates(new EntityCoordinates(linkGrid, emitter.LinkPosition)),
                emitter.MuzzleOffset, GetNetEntity(partner));
        }

        if (emitter.BeamGrid is not { } target ||
            TerminatingOrDeleted(target) || !_miningGrids.TryComp(target, out var grid) ||
            !_miningTransforms.TryComp(target, out var targetXform) || targetXform.MapUid != ent.Comp.MapUid)
            return null;

        return new BulkAutoMiningRadarBeam(
            GetNetCoordinates(_miningMap.GridTileToLocal(target, grid, emitter.BeamTile)), emitter.MuzzleOffset);
    }
}
