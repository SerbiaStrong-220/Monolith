using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    public bool TrySelectGrid(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target)
    {
        if (ent.Comp.Active)
        {
            Popup(ent, "bulk-auto-mining-select-while-active");
            return false;
        }

        // Allow removing a stale selection even after it moves out of range.
        if (ent.Comp.SelectedGrids.Remove(target))
        {
            ent.Comp.TotalTiles = 0;
            ent.Comp.ProcessedTiles = 0;
            UpdateUi(ent);
            return true;
        }

        if (!IsPoweredAndAnchored(ent) || !IsTargetInRange(ent, target))
        {
            Popup(ent, "bulk-auto-mining-select-out-of-range");
            return false;
        }

        if (!TryComp<BulkAutoMiningJobComponent>(ent, out var job))
            return false;

        ResolveEmitters(ent, job);
        if (ent.Comp.SelectedGrids.Count >= job.Emitters.Count)
        {
            Popup(ent, "bulk-auto-mining-start-no-emitter");
            return false;
        }

        ent.Comp.SelectedGrids.Add(target);
        ent.Comp.TotalTiles = 0;
        ent.Comp.ProcessedTiles = 0;
        foreach (var emitter in job.Emitters)
        {
            if (_emitterQuery.TryComp(emitter, out var comp) && IsForbiddenGrid(target, comp))
            {
                Popup(ent, "bulk-auto-mining-warning-forbidden-target");
                break;
            }
        }

        UpdateUi(ent);
        return true;
    }

    private bool IsTargetInRange(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !TryComp<MapGridComponent>(target, out var grid) ||
            !_xformQuery.TryComp(target, out var targetXform))
            return false;

        var consoleXform = Transform(ent);
        if (consoleXform.MapUid == null || consoleXform.MapUid != targetXform.MapUid || consoleXform.GridUid == target)
            return false;

        var position = Vector2.Transform(_transform.GetWorldPosition(consoleXform), _transform.GetInvWorldMatrix(target));
        var nearest = Vector2.Clamp(position, grid.LocalAABB.BottomLeft, grid.LocalAABB.TopRight);
        return Vector2.DistanceSquared(position, nearest) <= ent.Comp.MaxRange * ent.Comp.MaxRange;
    }

    private bool TryPrepareGrid(EntityUid target, [NotNullWhen(true)] out BulkAutoMiningGridJob? job)
    {
        job = null;
        if (TerminatingOrDeleted(target) || !TryComp<MapGridComponent>(target, out var grid))
            return false;

        // Materialize this selected grid once, not all chunks around every target on every mining tick.
        // Remove the loader before raising the event so another console cannot populate it twice.
        if (RemComp<LocalityLoaderComponent>(target))
            RaiseLocalEvent(target, new LocalStructureLoadedEvent());

        if (TerminatingOrDeleted(target))
            return false;

        var tiles = new Queue<Vector2i>();
        var remaining = new HashSet<Vector2i>();
        var rangeTiles = new List<Vector2i>();
        var enumerator = _map.GetAllTilesEnumerator(target, grid);
        while (enumerator.MoveNext(out var tile))
        {
            if (tile is { } tileRef && !tileRef.Tile.IsEmpty)
            {
                tiles.Enqueue(tileRef.GridIndices);
                remaining.Add(tileRef.GridIndices);
                rangeTiles.Add(tileRef.GridIndices);
            }
        }

        if (tiles.Count == 0)
            return false;

        job = new BulkAutoMiningGridJob
        {
            GridUid = target,
            Tiles = tiles,
            RemainingTiles = remaining,
            RangeTiles = rangeTiles,
        };
        return true;
    }

    private bool IsForbiddenGrid(EntityUid grid, BulkAutoMiningEmitterComponent emitter)
    {
        return HasComp<StationMemberComponent>(grid) ||
               TryComp<IFFComponent>(grid, out var iff) && ((iff.Flags & IFFFlags.IsPlayerShuttle) != 0 || iff.ReadOnly) ||
               _whitelist.IsBlacklistPass(emitter.ForbiddenTargets, grid);
    }
}
