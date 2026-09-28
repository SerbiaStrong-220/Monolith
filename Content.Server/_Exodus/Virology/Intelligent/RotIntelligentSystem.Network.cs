using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.StationAi;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    private void OnMemberAnchored(Entity<RotColonyMemberComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (ent.Comp.Core is { } core && ent.Comp.Size != Vector2i.Zero && IsActiveCore(core))
            MarkNetworkDirty(core);
    }

    private void OnMemberMoved(Entity<RotColonyMemberComponent> ent, ref MoveEvent args)
    {
        // Turning a one-cell organ does not change its occupied tiles or connectivity.
        if (!args.ParentChanged && args.OldPosition == args.NewPosition
            && (ent.Comp.Size == Vector2i.One && !ent.Comp.RequiresExhaust
                || RotGeometry.QuarterTurns(args.OldRotation) == RotGeometry.QuarterTurns(args.NewRotation)))
            return;
        if (ent.Comp.Core is { } core && ent.Comp.Size != Vector2i.Zero && IsActiveCore(core))
            MarkNetworkDirty(core);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var query = EntityQueryEnumerator<RotColonyStateComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (state.Grid != args.Entity)
                continue;
            foreach (var change in args.Changes)
            {
                if (_turf.IsSpace(change.OldTile) == _turf.IsSpace(change.NewTile)
                    || (!state.WatchedCells.Contains(change.GridIndices) && !state.BuildingWatchedCells.Contains(change.GridIndices)))
                    continue;
                MarkNetworkDirty(uid);
                break;
            }
        }
    }

    private void OnMemberInit(Entity<RotColonyMemberComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Core is { } core && IsLivingCore(core))
            Join(ent, core);
        else
            RefreshVision(ent);
    }

    private void OnMemberShutdown(Entity<RotColonyMemberComponent> ent, ref ComponentShutdown args)
    {
        _spread.InvalidateCell((ent.Owner, Transform(ent)));
        if (ent.Comp.Core is not { } core || !TryComp<RotColonyStateComponent>(core, out var state))
            return;
        state.Members.Remove(ent);
        state.Alerts.Remove(ent);
        if (ent.Comp.Size != Vector2i.Zero)
            MarkNetworkDirty(core);
        else
            RestartMemberPass((core, state));
    }

    private void OnMemberParent(Entity<RotColonyMemberComponent> ent, ref EntParentChangedMessage args)
    {
        RefreshVision(ent);
        if (ent.Comp.NeedsSupport && ent.Comp.Core is { } core)
            MarkNetworkDirty(core);
    }

    private void OnMemberState(Entity<RotColonyMemberComponent> ent, ref MobStateChangedEvent args) => RefreshVision(ent);

    private void OnMemberDamaged(Entity<RotColonyMemberComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || ent.Comp.Core is not { } core || !IsLivingCore(core)
            || !TryComp<RotColonyStateComponent>(core, out var state))
            return;
        state.Alerts[ent] = _timing.CurTime + TimeSpan.FromSeconds(15);
        state.NextUi = TimeSpan.Zero;
        foreach (var uid in state.Projects)
        {
            if (TryComp<RotConstructionComponent>(uid, out var job) && job.Kind == RotProjectKind.Repair && job.Target == ent.Owner)
            {
                CancelProject(uid, refund: true);
                break;
            }
        }
    }

    public void MarkNetworkDirty(EntityUid core, bool connectivityLost = true)
    {
        if (!_coreQuery.TryComp(core, out var intelligent) || !intelligent.Alive
            || !_colonyQuery.TryComp(core, out var state))
            return;
        state.DirtyNetwork = true;
        intelligent.NetworkReady = false;
        if (connectivityLost)
        {
            intelligent.ConstructionAvailable = false;
            intelligent.Income = intelligent.Rooted ? intelligent.BaseIncome : 0;
            // A burst of tile removals needs only one synchronous stop. Applying any supported
            // connection resets this flag, including during a partially completed rebuild.
            if (!state.SupportDisconnected)
            {
                state.SupportDisconnected = true;
                foreach (var uid in state.Members)
                {
                    if (_members.TryComp(uid, out var member) && member.NeedsSupport && member.Connected)
                        SetConnected((uid, member), false);
                }
            }
        }
        Dirty(core, intelligent);
    }

    private void RestartMemberPass(Entity<RotColonyStateComponent> ent)
    {
        // Mobile creatures change membership without changing the tissue graph. Invalidate only the live iterator.
        if (ent.Comp.NetworkPhase is not (RotNetworkPhase.Index or RotNetworkPhase.Apply or RotNetworkPhase.Refresh))
            return;
        ent.Comp.NetworkEnumerator.Dispose();
        ent.Comp.NetworkEnumerator = ent.Comp.Members.GetEnumerator();
        if (ent.Comp.NetworkPhase == RotNetworkPhase.Apply && TryComp<RotIntelligentComponent>(ent, out var core))
            ent.Comp.NetworkIncome = core.BaseIncome;
    }

    private void SetConnected(Entity<RotColonyMemberComponent> ent, bool connected)
    {
        var changed = ent.Comp.Connected != connected;
        if (changed && _nurseryQuery.TryComp(ent, out var nursery))
        {
            if (ent.Comp.Connected)
                nursery.Remaining = TimeSpan.FromTicks(Math.Max(0, nursery.Remaining.Ticks - (_timing.CurTime - nursery.LastUpdate).Ticks));
            nursery.LastUpdate = _timing.CurTime;
        }
        ent.Comp.Connected = connected;
        if (connected && ent.Comp.NeedsSupport && ent.Comp.Core is { } core
            && _colonyQuery.TryComp(core, out var state))
            state.SupportDisconnected = false;

        if (_thrusterQuery.TryComp(ent, out var thruster))
            _thrusters.SetEnabled((ent, thruster), connected && _shuttleQuery.HasComp(Transform(ent).GridUid));
        _appearance.SetData(ent, RotOrganVisuals.Connected, connected);
        RefreshVision(ent);
        if (changed)
        {
            Dirty(ent);
            var ev = new RotConnectionChangedEvent(connected);
            RaiseLocalEvent(ent, ref ev);
        }
    }

    private void RefreshVision(Entity<RotColonyMemberComponent> ent)
    {
        // Parent and mob-state events also fire while loading uninitialized maps.
        if (LifeStage(ent) != EntityLifeStage.MapInitialized)
            return;

        if (ent.Comp.VisionRange <= 0 && !_visionQuery.HasComp(ent))
            return;
        var enabled = ent.Comp.VisionRange > 0 && ent.Comp.Core is { } core && IsActiveCore(core)
            && Transform(ent).MapUid == Transform(core).MapUid
            && (!ent.Comp.NeedsSupport || ent.Comp.Connected) && !_containers.IsEntityInContainer(ent)
            && (!_mobQuery.HasComp(ent) || _mobs.IsAlive(ent));
        var vision = EnsureComp<StationAiVisionComponent>(ent);
        // A detached organic source stays in a private disabled network instead of becoming an AI camera.
        _ai.SetVisionNetwork((ent, vision), ent.Comp.Core ?? ent.Owner, ent.Comp.VisionRange, enabled);
    }

    private int UpdateNetwork(Entity<RotIntelligentComponent, RotColonyStateComponent> ent, int budget)
    {
        var state = ent.Comp2;
        if (state.DirtyNetwork)
        {
            state.DirtyNetwork = false;
            state.BuildingCells.Clear();
            state.BuildingConnected.Clear();
            state.Frontier.Clear();
            state.Seen.Clear();
            state.BuildingWatchedCells.Clear();
            state.NetworkEnumerator.Dispose();
            state.NetworkEnumerator = state.Members.GetEnumerator();
            state.NetworkPhase = RotNetworkPhase.Index;
            state.Grid = Transform(ent).GridUid;
        }
        if (state.Grid is not { } grid || !_mapGridQuery.TryComp(grid, out var mapGrid))
        {
            state.NetworkPhase = RotNetworkPhase.Idle;
            return 0;
        }
        if (state.NetworkPhase == RotNetworkPhase.Idle && state.RefreshPending)
        {
            state.RefreshPending = false;
            state.NetworkEnumerator = state.Members.GetEnumerator();
            state.NetworkPhase = RotNetworkPhase.Refresh;
        }

        var remaining = budget;
        while (remaining > 0)
        {
            switch (state.NetworkPhase)
            {
                case RotNetworkPhase.Index:
                    if (state.NetworkEnumerator.MoveNext())
                    {
                        remaining--;
                        var uid = state.NetworkEnumerator.Current;
                        if (!_members.TryComp(uid, out var member) || !_transforms.TryComp(uid, out var xform)
                            || xform.GridUid != grid || !xform.Anchored || TerminatingOrDeleted(uid))
                            break;
                        var origin = _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
                        var rotation = RotGeometry.QuarterTurns(xform.LocalRotation);
                        if (member.RequiresExhaust)
                            state.BuildingWatchedCells.Add(origin + RotGeometry.Rotate(new Vector2i(0, member.Size.Y), rotation));
                        foreach (var cell in RotGeometry.Cells(origin, member.Size, rotation))
                        {
                            state.BuildingWatchedCells.Add(cell);
                            if (_turf.IsSpace(_maps.GetTileRef(grid, mapGrid, cell)))
                                continue;
                            state.BuildingCells.TryGetValue(cell, out var conductive);
                            state.BuildingCells[cell] = conductive || member.Conductive;
                        }
                        break;
                    }
                    state.NetworkEnumerator.Dispose();
                    var start = _maps.TileIndicesFor(grid, mapGrid, Transform(ent).Coordinates);
                    state.Frontier.Enqueue(start);
                    state.Seen.Add(start);
                    state.NetworkPhase = RotNetworkPhase.Flood;
                    break;
                case RotNetworkPhase.Flood:
                    if (state.Frontier.TryDequeue(out var tile))
                    {
                        remaining--;
                        state.BuildingConnected.Add(tile);
                        foreach (var offset in Neighbors)
                        {
                            var neighbor = tile + offset;
                            if (!state.BuildingCells.TryGetValue(neighbor, out var conductive) || !conductive || !state.Seen.Add(neighbor))
                                continue;
                            state.Frontier.Enqueue(neighbor);
                        }
                        break;
                    }
                    // Publish a complete graph; placement never observes a half-finished flood fill.
                    (state.Cells, state.BuildingCells) = (state.BuildingCells, state.Cells);
                    (state.Connected, state.BuildingConnected) = (state.BuildingConnected, state.Connected);
                    state.NetworkEnumerator = state.Members.GetEnumerator();
                    state.NetworkIncome = ent.Comp1.BaseIncome;
                    state.NetworkPhase = RotNetworkPhase.Apply;
                    break;
                case RotNetworkPhase.Apply:
                    if (state.NetworkEnumerator.MoveNext())
                    {
                        remaining--;
                        ApplyNetworkMember(ent, state.NetworkEnumerator.Current);
                        break;
                    }
                    state.NetworkEnumerator.Dispose();
                    state.NetworkPhase = RotNetworkPhase.Idle;
                    (state.WatchedCells, state.BuildingWatchedCells) = (state.BuildingWatchedCells, state.WatchedCells);
                    state.BuildingWatchedCells.Clear();
                    ent.Comp1.NetworkReady = true;
                    ent.Comp1.ConstructionAvailable = true;
                    ent.Comp1.Income = state.NetworkIncome;
                    Dirty(ent.Owner, ent.Comp1);
                    state.NextVision = state.NextUi = TimeSpan.Zero;
                    ValidateProjects(ent);
                    return budget - remaining;
                case RotNetworkPhase.Refresh:
                    if (state.NetworkEnumerator.MoveNext())
                    {
                        remaining--;
                        var uid = state.NetworkEnumerator.Current;
                        if (!_members.TryComp(uid, out var member) || TerminatingOrDeleted(uid) || member.Core != ent.Owner)
                            break;
                        RefreshVision((uid, member));
                        if (_thrusterQuery.TryComp(uid, out var thruster))
                            _thrusters.SetEnabled((uid, thruster), member.Connected && _shuttleQuery.HasComp(Transform(uid).GridUid));
                        break;
                    }
                    state.NetworkEnumerator.Dispose();
                    state.NetworkPhase = RotNetworkPhase.Idle;
                    return budget - remaining;
                default:
                    return budget - remaining;
            }
        }
        return budget;
    }

    private void ApplyNetworkMember(Entity<RotIntelligentComponent, RotColonyStateComponent> ent, EntityUid uid)
    {
        var state = ent.Comp2;
        if (!_members.TryComp(uid, out var member) || TerminatingOrDeleted(uid))
            return;
        var connected = !member.NeedsSupport || IsSupported((uid, member), state);
        SetConnected((uid, member), connected);
        if (member.Tissue && state.Grid is { } grid && _mapGridQuery.TryComp(grid, out var mapGrid)
            && Transform(uid).GridUid == grid)
        {
            var tile = _maps.TileIndicesFor(grid, mapGrid, Transform(uid).Coordinates);
            var mask = 0;
            for (var i = 0; i < Neighbors.Length; i++)
            {
                if (state.Cells.ContainsKey(tile + Neighbors[i]))
                    mask |= 1 << i;
            }
            // RSI order is north, east, south, west in image coordinates (Y points down).
            mask = (mask & 10) | ((mask & 1) << 2) | ((mask & 4) >> 2);
            _appearance.SetData(uid, RotOrganVisuals.Connections, mask);
        }
        if (connected)
            state.NetworkIncome += member.Income;
    }

    private bool IsSupported(Entity<RotColonyMemberComponent> ent, RotColonyStateComponent state)
    {
        var xform = Transform(ent);
        if (state.Grid is not { } grid || xform.GridUid != grid || !xform.Anchored
            || !_mapGridQuery.TryComp(grid, out var mapGrid))
            return false;
        var origin = _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
        if (ent.Comp.RequiresExhaust)
        {
            var nozzle = origin + RotGeometry.Rotate(new Vector2i(0, ent.Comp.Size.Y), RotGeometry.QuarterTurns(xform.LocalRotation));
            if (!_turf.IsSpace(_maps.GetTileRef(grid, mapGrid, nozzle)))
                return false;
        }
        foreach (var tile in RotGeometry.Cells(origin, ent.Comp.Size, RotGeometry.QuarterTurns(xform.LocalRotation)))
        {
            if (!state.Connected.Contains(tile))
                return false;
        }
        return true;
    }

    private void RefreshMembers(Entity<RotIntelligentComponent, RotColonyStateComponent> ent)
    {
        ent.Comp2.RefreshPending = true;
        ent.Comp2.Scratch.Clear();
        foreach (var (uid, until) in ent.Comp2.Alerts)
        {
            if (until <= _timing.CurTime || TerminatingOrDeleted(uid))
                ent.Comp2.Scratch.Add(uid);
        }
        foreach (var uid in ent.Comp2.Scratch)
            ent.Comp2.Alerts.Remove(uid);
    }
}
