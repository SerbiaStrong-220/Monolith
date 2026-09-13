using System.Threading;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;

    private void BeginDetour(Entity<RotHungryComponent> ent)
    {
        CancelDetour(ent);
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        ent.Comp.ClearingObstacle = false;
        ent.Comp.ObstructionSince = null;
        ent.Comp.DetourUntil = _timing.CurTime + ent.Comp.DetourDuration;
        ent.Comp.DetourCancellation = new CancellationTokenSource();
        // Native breadth-first navigation chooses a reachable side route without treating obstacles as traversable.
        ent.Comp.DetourPath = _pathfinding.GetRandomPath(ent, ent.Comp.DetourRange,
            ent.Comp.DetourCancellation.Token, limit: 100, flags: PathFlags.None);
    }

    private bool UpdateDetour(Entity<RotHungryComponent> ent)
    {
        if (ent.Comp.DetourPath == null && ent.Comp.DetourPoint == null)
            return false;

        ent.Comp.ClearingObstacle = false;
        RemComp<NPCMeleeCombatComponent>(ent);
        if (_timing.CurTime >= ent.Comp.DetourUntil)
        {
            CancelDetour(ent);
            _steering.Unregister(ent);
            return false;
        }

        if (ent.Comp.DetourPath is { } task)
        {
            if (!task.IsCompleted)
                return true;

            ent.Comp.DetourPath = null;
            if (!task.IsCompletedSuccessfully)
            {
                // Observe failed requests before returning to normal movement.
                _ = task.Exception;
                CancelDetour(ent);
                return true;
            }

            // The task has completed successfully; reading its result cannot block the server thread.
#pragma warning disable RA0004
            var result = task.Result;
#pragma warning restore RA0004
            if (result.Result != PathResult.Path || result.Path.Count == 0)
            {
                CancelDetour(ent);
                return false;
            }
            var path = result.Path;
            var point = path[^1].Coordinates;
            if (!point.IsValid(EntityManager))
            {
                CancelDetour(ent);
                return false;
            }
            ent.Comp.DetourPoint = point;
            var steering = _steering.Register(ent, point);
            steering.Flags = PathFlags.None;
            steering.Range = 0.4f;
            steering.CurrentPath = new(path);
        }

        if (ent.Comp.DetourPoint is not { } destination || !destination.IsValid(EntityManager)
            || !TryComp<NPCSteeringComponent>(ent, out var current) || current.Status != SteeringStatus.Moving)
        {
            CancelDetour(ent);
            _steering.Unregister(ent);
            ent.Comp.LastMoved = _timing.CurTime;
            ent.Comp.LastPosition = Transform(ent).Coordinates;
            return false;
        }

        return true;
    }

    private void CancelDetour(Entity<RotHungryComponent> ent)
    {
        ent.Comp.DetourCancellation?.Cancel();
        ent.Comp.DetourCancellation?.Dispose();
        ent.Comp.DetourCancellation = null;
        ent.Comp.DetourPath = null;
        ent.Comp.DetourPoint = null;
    }
}
