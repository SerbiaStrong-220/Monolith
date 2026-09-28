using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private RotColonySiteSystem _sites = default!;
    private readonly List<Entity<RotColonySiteComponent, TransformComponent>> _nestSites = [];

    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private RotGroundStrikeSystem _groundStrike = default!;

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

        if (now - ent.Comp.LastMoved < ent.Comp.StuckDelay || !HasComp<MeleeWeaponComponent>(ent))
            return false;

        if (ent.Comp.ClearingObstacle && now < ent.Comp.NextStuckAttack)
            return true;

        if (!_groundStrike.HasObstacle(ent, ent.Comp.StuckTileRadius))
            return false;

        // Both combat and native path clearing can consume the melee cooldown with missed swings.
        // Keep them paused between ground strikes so missed path-clearing swings cannot steal the next cooldown.
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        _combat.SetInCombatMode(ent, true);
        if (now < ent.Comp.NextStuckAttack)
            return true;

        if (!_groundStrike.TryStrike(ent, ent.Comp.StuckTileRadius, ent.Comp.StuckDamage, ent.Comp.StuckSound))
        {
            ent.Comp.NextStuckAttack = now + ent.Comp.ThinkInterval;
            return true;
        }
        ent.Comp.ObstructionSince ??= now;
        ent.Comp.NextStuckAttack = now + ent.Comp.StuckAttackInterval;
        return true;
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
        _sites.GetSites(ent, _nestSites);
        foreach (var (uid, _, siteTransform) in _nestSites)
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
