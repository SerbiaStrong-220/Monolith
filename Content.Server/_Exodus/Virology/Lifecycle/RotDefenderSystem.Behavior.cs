using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotDefenderSystem
{
    [Dependency] private RotEscapeSystem _escape = default!;

    private bool RouteAvoidsThreats(Entity<RotDefenderComponent> ent, PathResultEvent path)
    {
        if (!ent.Comp.AvoidThreatsOnRoutes)
            return true;
        var origin = _transform.GetMapCoordinates(ent);
        foreach (var enemy in ent.Comp.Enemies)
        {
            if (!IsEnemy(enemy))
                continue;
            var threat = _transform.GetMapCoordinates(enemy);
            if (threat.MapId != origin.MapId)
                continue;
            if (!_escape.PathAvoidsThreat(path, threat, Vector2.Distance(origin.Position, threat.Position)))
                return false;
        }
        return true;
    }

    /// <summary>Shared pursuit and obstruction handling for defenders with a different target-selection policy.</summary>
    public bool PursueDefenderTarget(Entity<RotDefenderComponent> ent, EntityUid target)
    {
        if (!CanPursue(ent, target) || IsRejectedEnemy(ent, target))
            return false;
        if (ent.Comp.Target != target)
        {
            CancelRoute(ent);
            ent.Comp.Target = target;
            ent.Comp.DetouredTarget = null;
            ResetProgress(ent);
        }
        if (TryComp<NPCSteeringComponent>(ent, out var steering) && steering.Status == SteeringStatus.NoPath
            && !_groundStrike.HasObstacle(ent, ent.Comp.StuckTileRadius))
        {
            RejectEnemy(ent, target);
            return false;
        }
        if (ent.Comp.Route == RotDefenderRoute.Detour)
        {
            if (UpdateRoute(ent))
                return true;
            CancelRoute(ent);
        }
        if (HandleStuckMovement(ent, new EntityCoordinates(target, Vector2.Zero), 0.8f))
            return true;
        Fight(ent);
        return true;
    }

    /// <summary>Damaged waiting creatures leave immediately; damage during a route does not starve pathfinding.</summary>
    public bool AvoidDefenderThreat(Entity<RotDefenderComponent> ent, EntityUid? threat, bool force)
    {
        var damaged = ent.Comp.DamagePending;
        var environmental = ent.Comp.EnvironmentalDamage;
        ent.Comp.DamagePending = ent.Comp.EnvironmentalDamage = false;
        if (!force && !damaged && ent.Comp.Route == RotDefenderRoute.None)
            return false;
        ent.Comp.Target = null;
        if (threat is { } source && !TerminatingOrDeleted(source))
            ent.Comp.Threat = source;
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        if (ent.Comp.MovementRequested && ent.Comp.Route != RotDefenderRoute.None
            && ent.Comp.EscapePath == null && ent.Comp.EscapePoint == null
            && HandleStuckMovement(ent, null, 0.5f))
            return true;
        if (ent.Comp.EscapePath != null || ent.Comp.EscapePoint != null)
        {
            UpdateRoute(ent);
            return true;
        }
        if (damaged)
        {
            var route = ent.Comp.Route == RotDefenderRoute.Colony ? RotDefenderRoute.Cover
                : environmental || ent.Comp.Route == RotDefenderRoute.Cover ? RotDefenderRoute.Colony : RotDefenderRoute.Cover;
            BeginRoute(ent, ent.Comp.Threat, route);
            return true;
        }
        if (_timing.CurTime < ent.Comp.RestUntil)
        {
            if (ent.Comp.Route != RotDefenderRoute.Cover || IsCovered(ent, Transform(ent).Coordinates))
                return true;
            BeginRoute(ent, ent.Comp.Threat, RotDefenderRoute.Cover);
            return true;
        }
        if (force || ent.Comp.RouteFailed)
        {
            BeginRoute(ent, ent.Comp.Threat, force ? RotDefenderRoute.Cover : ent.Comp.Route);
            return true;
        }
        CancelRoute(ent);
        return false;
    }

    public void MoveDefender(Entity<RotDefenderComponent> ent, EntityCoordinates destination, float range)
    {
        if (ent.Comp.Route == RotDefenderRoute.Detour && UpdateRoute(ent))
            return;
        ent.Comp.Target = null;
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        if (!HandleStuckMovement(ent, destination, range))
            Move(ent, destination, range);
    }
}
