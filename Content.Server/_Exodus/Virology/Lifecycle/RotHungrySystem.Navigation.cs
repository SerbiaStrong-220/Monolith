using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private void OnDoorOpening(Entity<DoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.User is not { } user || HasComp<ActorComponent>(user)
            || !TryComp<RotHungryComponent>(user, out var hungry) || hungry.Target == null || hungry.Retreating)
            return;

        // Hunting NPCs break closed doors; player-controlled ghost roles retain normal interaction.
        args.Cancel();
    }

    /// <returns>Whether clearing an obstacle takes precedence over hunting this update.</returns>
    private bool HandleStuckMovement(Entity<RotHungryComponent> ent)
    {
        var now = _timing.CurTime;
        var transform = Transform(ent);
        var coordinates = transform.Coordinates;
        if (ent.Comp.Destination is not { } destination
            || !ent.Comp.LastPosition.TryDistance(EntityManager, coordinates, out var moved) || moved > 0.25f)
        {
            ent.Comp.LastPosition = coordinates;
            ent.Comp.LastMoved = now;
            ent.Comp.ObstructionSince = null;
            return false;
        }

        var range = TryComp<NPCSteeringComponent>(ent, out var steering) ? steering.Range : 1f;
        if (coordinates.TryDistance(EntityManager, destination, out var distance) && distance <= range
            && _interaction.InRangeUnobstructed(ent, destination, range))
        {
            ent.Comp.LastMoved = now;
            ent.Comp.ObstructionSince = null;
            return false;
        }

        if (ent.Comp.ObstructionSince is { } obstruction && now - obstruction >= ent.Comp.DetourDelay)
        {
            BeginDetour(ent);
            return false;
        }

        if (now - ent.Comp.LastMoved < ent.Comp.StuckDelay || !TryComp<MeleeWeaponComponent>(ent, out var weapon)
            || !_physicsQuery.TryComp(ent, out var body) || transform.GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;

        if (ent.Comp.ClearingObstacle && now < ent.Comp.NextStuckAttack)
            return true;

        // Query tile bounds in grid space: offset railing fixtures and rotated ships must be included.
        var tile = _map.TileIndicesFor(grid, mapGrid, coordinates);
        var radius = ent.Comp.StuckTileRadius;
        var minimum = new Vector2(tile.X - radius, tile.Y - radius) * mapGrid.TileSize;
        var size = new Vector2((2 * radius + 1) * mapGrid.TileSize);
        _obstacles.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, Box2.FromDimensions(minimum, size), _obstacles, LookupFlags.Uncontained);
        var blocked = false;
        foreach (var (obstacle, _) in _obstacles)
        {
            if (obstacle == ent.Owner || _rotQuery.HasComp(obstacle) || !_physicsQuery.TryComp(obstacle, out var physics)
                || !physics.Hard || !physics.CanCollide || !IsInGroundStrikeArea(obstacle, (grid, mapGrid), tile, radius)
                || (physics.CollisionLayer & body.CollisionMask) == 0 && (physics.CollisionMask & body.CollisionLayer) == 0)
                continue;

            blocked = true;
            break;
        }

        if (!blocked)
            return false;

        // Both combat and native path clearing can consume the melee cooldown with missed swings.
        // Keep them paused between ground strikes so missed path-clearing swings cannot steal the next cooldown.
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        _combat.SetInCombatMode(ent, true);
        if (now < ent.Comp.NextStuckAttack)
            return true;

        if (!_melee.AttemptHeavyAttack(ent, ent, weapon, [], new EntityCoordinates(ent, 0f, -0.3f)))
        {
            ent.Comp.NextStuckAttack = now + ent.Comp.ThinkInterval;
            return true;
        }

        ent.Comp.ObstructionSince ??= now;
        ent.Comp.NextStuckAttack = now + ent.Comp.StuckAttackInterval;
        _audio.PlayPvs(ent.Comp.StuckSound, ent);
        foreach (var (obstacle, _) in _obstacles)
        {
            if (obstacle == ent.Owner || _rotQuery.HasComp(obstacle)
                || !IsInGroundStrikeArea(obstacle, (grid, mapGrid), tile, radius))
                continue;

            _damage.TryChangeDamage(obstacle, ent.Comp.StuckDamage, origin: ent.Owner);
        }

        return true;
    }

    private bool IsInGroundStrikeArea(EntityUid obstacle, Entity<MapGridComponent> grid, Vector2i center, int radius)
    {
        if (TerminatingOrDeleted(obstacle) || !_transformQuery.TryComp(obstacle, out var transform)
            || transform.GridUid != grid.Owner)
            return false;

        // Physics shapes can overlap a tile boundary; damage is limited to the selected tiles.
        var tile = _map.TileIndicesFor(grid.Owner, grid.Comp, transform.Coordinates);
        return Math.Abs(tile.X - center.X) <= radius && Math.Abs(tile.Y - center.Y) <= radius;
    }

    private EntityUid? FindNest(Entity<RotHungryComponent> ent, bool nurseryOnly)
    {
        var transform = Transform(ent);
        var grid = transform.GridUid;
        if (ent.Comp.Nest is { } cached && (TerminatingOrDeleted(cached)
            || _containers.IsEntityInContainer(cached) || !_transformQuery.TryComp(cached, out var cachedTransform)
            || cachedTransform.MapUid != transform.MapUid || cachedTransform.GridUid == null
            || grid != null && cachedTransform.GridUid != grid || nurseryOnly && !HasComp<RotNestComponent>(cached)))
            ent.Comp.Nest = null;

        if (_timing.CurTime < ent.Comp.NextNestSearch)
            return ent.Comp.Nest;

        // Recheck the nearest site periodically: a new nursery may be closer than the cached one.
        ent.Comp.Nest = null;
        ent.Comp.NextNestSearch = _timing.CurTime + TimeSpan.FromSeconds(5);

        var origin = _transform.GetMapCoordinates(ent);
        var nearest = float.MaxValue;
        var query = EntityQueryEnumerator<RotColonySiteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var siteTransform))
        {
            if (TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid)
                || siteTransform.GridUid == null || grid != null && siteTransform.GridUid != grid
                || nurseryOnly && !HasComp<RotNestComponent>(uid))
                continue;

            var position = _transform.GetMapCoordinates(uid, siteTransform);
            var distance = Vector2.DistanceSquared(origin.Position, position.Position);
            if (position.MapId != origin.MapId || distance >= nearest)
                continue;
            nearest = distance;
            ent.Comp.Nest = uid;
        }

        return ent.Comp.Nest;
    }

    private void ReturnHome(Entity<RotHungryComponent> ent)
    {
        RemComp<NPCMeleeCombatComponent>(ent);
        var home = FindNest(ent, false) is { } nest
            ? new EntityCoordinates(nest, Vector2.Zero)
            : ent.Comp.Home;
        if (home.IsValid(EntityManager))
            Move(ent, home, 2f);
        else
            Stop(ent);
    }

    private void Retreat(Entity<RotHungryComponent> ent)
    {
        RemComp<NPCMeleeCombatComponent>(ent);
        DropCorpse(ent);
        if (ent.Comp.Target is not { } target)
        {
            ReturnHome(ent);
            return;
        }

        if (_timing.CurTime >= ent.Comp.NextShelterSearch || ent.Comp.Shelter is not { } oldShelter || !oldShelter.IsValid(EntityManager))
        {
            ent.Comp.NextShelterSearch = _timing.CurTime + TimeSpan.FromSeconds(5);
            ent.Comp.Shelter = FindShelter(ent, target);
        }

        if (ent.Comp.Shelter is { } shelter)
            Move(ent, shelter, 0.6f);
        else
            ReturnHome(ent);
    }

    private EntityCoordinates? FindShelter(Entity<RotHungryComponent> ent, EntityUid threat)
    {
        var transform = Transform(ent);
        if (transform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return null;

        var origin = _transform.GetMapCoordinates(ent);
        var enemy = _transform.GetMapCoordinates(threat);
        var away = origin.Position - enemy.Position;
        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;
        away = Vector2.Normalize(away);
        var bestScore = float.MinValue;
        EntityCoordinates? result = null;
        // A small fixed set of retreat points; native pathfinding handles the route around cover.
        for (var i = 0; i < 12; i++)
        {
            var angle = i * MathF.Tau / 12f;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 6f;
            var point = new MapCoordinates(origin.Position + offset, origin.MapId);
            var local = _transform.ToCoordinates(grid, point);
            var indices = _map.TileIndicesFor(grid, mapGrid, local);
            var tile = _map.GetTileRef(grid, mapGrid, indices);
            if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;

            var score = Vector2.Dot(offset, away);
            if (!_interaction.InRangeUnobstructed(threat, local, 0))
                score += 12f;
            if (score <= bestScore)
                continue;
            bestScore = score;
            result = local;
        }

        return result;
    }

    private bool DeliverCorpse(Entity<RotHungryComponent> ent)
    {
        RemComp<NPCMeleeCombatComponent>(ent);
        if (ent.Comp.Corpse == null)
        {
            _removedPrey.Clear();
            foreach (var body in ent.Comp.Corpses)
            {
                if (TerminatingOrDeleted(body) || !_mobs.IsDead(body))
                {
                    _removedPrey.Add(body);
                    continue;
                }
                if (ent.Comp.Corpse == null && !_containers.IsEntityInContainer(body)
                    && Transform(body).GridUid == Transform(ent).GridUid)
                    ent.Comp.Corpse = body;
            }
            foreach (var body in _removedPrey)
                ent.Comp.Corpses.Remove(body);
        }

        if (ent.Comp.Corpse is not { } corpse || TerminatingOrDeleted(corpse) || !_mobs.IsDead(corpse)
            || _containers.IsEntityInContainer(corpse) || Transform(corpse).GridUid != Transform(ent).GridUid)
        {
            DropCorpse(ent);
            ent.Comp.Corpse = null;
            return false;
        }

        if (FindNest(ent, true) is not { } nest)
            return false;

        if (!TryComp<PullerComponent>(ent, out var puller))
            return false;
        if (puller.Pulling != corpse)
        {
            Move(ent, new EntityCoordinates(corpse, Vector2.Zero), 0.8f);
            if (!_interaction.InRangeUnobstructed(ent.Owner, corpse, 1.2f))
                return true;
            if (!_pulling.TryStartPull(ent, corpse))
                return false;
        }

        if (Transform(corpse).Coordinates.TryDistance(EntityManager, Transform(nest).Coordinates, out var distance) && distance <= 1.5f)
        {
            DropCorpse(ent);
            ent.Comp.Corpses.Remove(corpse);
            ent.Comp.Corpse = null;
            Stop(ent);
            return true;
        }

        Move(ent, new EntityCoordinates(nest, Vector2.Zero), 0.4f);
        return true;
    }

    private void DropCorpse(Entity<RotHungryComponent> ent)
    {
        if (TryComp<PullerComponent>(ent, out var puller) && puller.Pulling is { } body
            && TryComp<PullableComponent>(body, out var pullable))
            _pulling.TryStopPull(body, pullable, ent);
    }
}
