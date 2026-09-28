using System.Numerics;
using System.Threading;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private RotEscapeSystem _escape = default!;

    private void Retreat(Entity<RotHungryComponent> ent)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextRetreatUpdate)
            return;
        ent.Comp.NextRetreatUpdate = now + TimeSpan.FromSeconds(0.2);
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        DropCorpse(ent);
        if (ent.Comp.Target is not { } target || InvalidPrey(target))
        {
            CancelRetreatPath(ent);
            ReturnHome(ent);
            return;
        }

        if (ent.Comp.RetreatPath is { IsCompleted: true } task)
        {
            CancelRetreatPath(ent);
            if (task.IsCompletedSuccessfully)
            {
#pragma warning disable RA0004
                var result = task.Result;
#pragma warning restore RA0004
                if (result.Result == PathResult.Path && result.Path.Count > 0
                    && result.Path[^1].Coordinates is var point && point.IsValid(EntityManager)
                    && _escape.IsFurtherFromThreat(ent, target, point))
                {
                    var position = _transform.GetMapCoordinates(ent);
                    var offset = _transform.ToMapCoordinates(point).Position - position.Position;
                    // Pathfinding runs while we move; discard nodes already passed to avoid stepping backwards.
                    _steering.PrunePath(ent, position, offset, result.Path);
                    ent.Comp.RetreatDirection = Vector2.Normalize(offset);
                    ent.Comp.Shelter = point;
                    Move(ent, point, 0.6f);
                    var steering = Comp<NPCSteeringComponent>(ent);
                    steering.Status = SteeringStatus.Moving;
                    // At a real dead end, brake before drifting off the last floor tile into space.
                    steering.InRangeMaxSpeed = 0.1f;
                    steering.Flags = PathFlags.Interact | PathFlags.Smashing;
                    steering.CurrentPath = new(result.Path);
                }
            }
            else
            {
                _ = task.Exception;
            }
        }

        var hasRoute = ent.Comp.Shelter is { } shelter && shelter.IsValid(EntityManager)
            && Transform(ent).Coordinates.TryDistance(EntityManager, shelter, out var distance)
            && distance > ent.Comp.RetreatAdvanceRange && _escape.IsFurtherFromThreat(ent, target, shelter)
            && TryComp<NPCSteeringComponent>(ent, out var current) && current.Status == SteeringStatus.Moving;
        if (hasRoute || ent.Comp.RetreatPath != null || now < ent.Comp.NextShelterSearch)
            return;

        // Keep following the existing route while its continuation is being computed.
        // Arrivals and replans never restart the five-second combat decision timer.
        ent.Comp.NextShelterSearch = now + ent.Comp.RetreatRepathInterval;
        ent.Comp.RetreatCancellation = new CancellationTokenSource();
        ent.Comp.RetreatPath = _escape.FindPath(ent, target, ent.Comp.RetreatRange, ent.Comp.RetreatAdvanceRange,
            ent.Comp.RetreatDirection, ent.Comp.RetreatCancellation.Token);
    }

    private void CancelRetreatPath(Entity<RotHungryComponent> ent)
    {
        ent.Comp.RetreatCancellation?.Cancel();
        ent.Comp.RetreatCancellation?.Dispose();
        ent.Comp.RetreatCancellation = null;
        ent.Comp.RetreatPath = null;
    }
}
