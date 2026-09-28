using System.Numerics;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.EntityTable;
using Content.Shared.Examine;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private EntityTableSystem _tables = default!;

    private void InitializeBrood()
    {
        SubscribeLocalEvent<RotNurseryComponent, MapInitEvent>(OnNurseryInit);
        SubscribeLocalEvent<RotNurseryComponent, ExaminedEvent>(OnNurseryExamined);
    }

    private void OnNurseryInit(Entity<RotNurseryComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.LastUpdate = ent.Comp.NextUpdate = _timing.CurTime;
        if (ent.Comp.Selected != null)
            return;
        ent.Comp.Remaining = ent.Comp.Duration;
        foreach (var prototype in _tables.GetSpawns(ent.Comp.Offspring))
        {
            if (ent.Comp.Selected == null)
                ent.Comp.Selected = prototype;
            else
                ent.Comp.RemainingOffspring.Add(prototype);
        }
    }

    private void OnNurseryExamined(Entity<RotNurseryComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Remaining <= TimeSpan.Zero ? Loc.GetString("rot-nursery-blocked")
            : Loc.GetString("rot-nursery-progress", ("seconds", Math.Ceiling(ent.Comp.Remaining.TotalSeconds))));
        if (!_members.TryComp(ent, out var member) || !member.Connected)
            args.PushMarkup(Loc.GetString("rot-organ-disconnected"));
    }

    private void UpdateBrood()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RotNurseryComponent, RotColonyMemberComponent>();
        while (query.MoveNext(out var uid, out var nursery, out var member))
        {
            if (now < nursery.NextUpdate || nursery.Finished || TerminatingOrDeleted(uid))
                continue;
            nursery.NextUpdate = now + TimeSpan.FromSeconds(0.5);
            var elapsed = now - nursery.LastUpdate;
            nursery.LastUpdate = now;
            if (!member.Connected || !IsLivingCore(member.Core))
                continue;
            // Connectivity transitions settle active time; paused timestamps exclude map suspension.
            nursery.Remaining -= elapsed;
            if (nursery.Remaining < TimeSpan.Zero)
                nursery.Remaining = TimeSpan.Zero;
            var progress = 1 - nursery.Remaining / nursery.Duration;
            _appearance.SetData(uid, RotOrganVisuals.Growth, Math.Clamp((int)(progress * 3), 0, 3));
            if (nursery.Remaining > TimeSpan.Zero || nursery.Selected == null
                || Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
                continue;
            var tile = _maps.TileIndicesFor(grid, mapGrid, Transform(uid).Coordinates);
            // Blocked exits preserve the rest of the batch, including across map saves.
            // Bound unusually large entity-table results instead of spawning them all in one tick.
            for (var hatch = 0; hatch < 8 && nursery.Selected is { } selected; hatch++)
            {
                EntityCoordinates? spawn = null;
                for (var x = -1; x <= member.Size.X && spawn == null; x++)
                {
                    for (var y = -1; y <= member.Size.Y; y++)
                    {
                        var offset = RotGeometry.Rotate(new Vector2i(x, y), RotGeometry.QuarterTurns(Transform(uid).LocalRotation));
                        var point = _maps.GridTileToLocal(grid, mapGrid, tile + offset);
                        if (!IsSpawnClear(uid, (grid, mapGrid), point))
                            continue;
                        spawn = point;
                        break;
                    }
                }
                if (spawn is not { } coordinates)
                    break;
                var child = Spawn(selected, coordinates);
                if (TryComp<VirusReservoirComponent>(uid, out var reservoir) && reservoir.Strain is { } strain
                    && TryComp<VirusOffspringComponent>(child, out var offspring))
                    offspring.Strain = Lifecycle.VirusLifecycleSystem.FreshInfection(strain);
                Inherit(uid, child);
                if (nursery.RemainingOffspring.Count > 0)
                {
                    var last = nursery.RemainingOffspring.Count - 1;
                    nursery.Selected = nursery.RemainingOffspring[last];
                    nursery.RemainingOffspring.RemoveAt(last);
                    continue;
                }
                nursery.Selected = null;
                nursery.Finished = true;
                var effect = Spawn(nursery.HatchEffect, Transform(uid).Coordinates);
                _transform.SetLocalRotation(effect, Transform(uid).LocalRotation);
                QueueDel(uid);
            }
        }
    }

    private bool IsSpawnClear(EntityUid nursery, Entity<MapGridComponent> grid, EntityCoordinates point)
    {
        if (_turf.IsSpace(_maps.GetTileRef(grid, grid.Comp, point)))
            return false;
        _placement.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid.Owner, Box2.CenteredAround(point.Position, new Vector2(0.8f)), _placement);
        foreach (var uid in _placement)
        {
            if (uid == nursery || TerminatingOrDeleted(uid))
                continue;
            if (_members.TryComp(uid, out var member) && member.Tissue)
                continue;
            if (HasComp<Content.Shared.Mobs.Components.MobStateComponent>(uid) && !_mobs.IsDead(uid))
                return false;
            if (TryComp<PhysicsComponent>(uid, out var physics) && physics.CanCollide
                && (physics.CollisionLayer & (int)CollisionGroup.FullTileMask) != 0)
                return false;
        }
        return true;
    }
}
