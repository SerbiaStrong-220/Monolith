using System.Numerics;
using System.Threading;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Gives armed structures a bounded melee attempt, then escapes without restarting on each shot.</summary>
public sealed partial class RotRetaliationSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private RotEscapeSystem _escape = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotRetaliationComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<RotRetaliationComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RotRetaliationComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RotRetaliationComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnDamaged(Entity<RotRetaliationComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !CanAct(ent) || !TryComp<HTNComponent>(ent, out var htn) || !htn.Enabled)
            return;

        // Environmental damage in a retreat must not leave a creature waiting at an unsafe waypoint.
        if (ent.Comp.Retreating)
            ent.Comp.RetreatUntil = _timing.CurTime + ent.Comp.RetreatDuration;

        var source = args.Origin;
        if (!IsAttacker(ent, source) && TryComp<ProjectileComponent>(args.Tool, out var projectile))
            source = projectile.Weapon;
        if (!IsAttacker(ent, source))
            return;

        if (ent.Comp.Target == null)
        {
            ent.Comp.Target = source;
            ent.Comp.ReachBy = _timing.CurTime + ent.Comp.ReachTimeout;
            ent.Comp.NextUpdate = _timing.CurTime;
            ent.Comp.NextPathSearch = _timing.CurTime;
            _htn.Replan(htn);
        }
        // A second turret cannot grant another four seconds of standing in the crossfire.
        // Do not starve asynchronous pathfinding by cancelling it on every alternating turret hit.
        else if (ent.Comp.Retreating && ent.Comp.Target != source && ent.Comp.Path == null
            && _timing.CurTime >= ent.Comp.NextPathSearch)
        {
            ent.Comp.Target = source;
            ent.Comp.Destination = null;
        }
    }

    private bool CanAct(EntityUid uid) => !TerminatingOrDeleted(uid) && _mobs.IsAlive(uid)
        && !HasComp<ActorComponent>(uid) && !_containers.IsEntityInContainer(uid);

    private bool IsAttacker(Entity<RotRetaliationComponent> ent, EntityUid? uid) => uid is { } target
        && target != ent.Owner && !TerminatingOrDeleted(target) && !HasComp<RotCreatureComponent>(target)
        && !HasComp<MobStateComponent>(target) && HasComp<DamageableComponent>(target)
        && !_containers.IsEntityInContainer(target) && _whitelist.IsValid(ent.Comp.Attackers, target);

    public void Begin(EntityUid uid)
    {
        // A defender hauling a corpse must be able to fight or escape without dragging it along.
        if (TryComp<PullerComponent>(uid, out var puller) && puller.Pulling is { } body
            && TryComp<PullableComponent>(body, out var pullable))
            _pulling.TryStopPull(body, pullable, uid);
    }

    public bool Think(Entity<RotRetaliationComponent> ent)
    {
        if (!CanAct(ent) || !IsAttacker(ent, ent.Comp.Target) || !TryComp<MeleeWeaponComponent>(ent, out var weapon))
        {
            Clear(ent);
            return false;
        }
        var target = ent.Comp.Target!.Value;
        if (_transform.GetMapCoordinates(ent).MapId != _transform.GetMapCoordinates(target).MapId)
        {
            Clear(ent);
            return false;
        }
        var now = _timing.CurTime;
        if (now < ent.Comp.NextUpdate)
            return true;
        ent.Comp.NextUpdate = now + TimeSpan.FromSeconds(0.2);

        if (!ent.Comp.Retreating)
        {
            var reachable = _interaction.InRangeUnobstructed(ent.Owner, target, weapon.Range);
            if (reachable)
                ent.Comp.ReachBy = now + ent.Comp.ReachTimeout;
            if (now >= ent.Comp.ReachBy)
            {
                StopMovement(ent);
                ent.Comp.Retreating = true;
                ent.Comp.RetreatUntil = now + ent.Comp.RetreatDuration;
                ent.Comp.NextPathSearch = now;
            }
            else
            {
                Pursue(ent, target, weapon.Range, reachable);
                return true;
            }
        }

        if (now >= ent.Comp.RetreatUntil)
        {
            Clear(ent);
            return false;
        }
        Retreat(ent, target);
        return true;
    }

    private void Pursue(Entity<RotRetaliationComponent> ent, EntityUid target, float range, bool reachable)
    {
        if (reachable)
        {
            CancelPath(ent);
            EnsureComp<NPCMeleeCombatComponent>(ent).Target = target;
            var steering = _steering.Register(ent, new EntityCoordinates(target, Vector2.Zero));
            steering.Range = range * 0.9f;
            return;
        }

        if (ent.Comp.Path is { IsCompleted: true } task)
        {
            CancelPath(ent);
            if (task.IsCompletedSuccessfully)
            {
#pragma warning disable RA0004
                var result = task.Result;
#pragma warning restore RA0004
                if (result.Result == PathResult.Path && result.Path.Count > 0)
                {
                    EnsureComp<NPCMeleeCombatComponent>(ent).Target = target;
                    var steering = _steering.Register(ent, new EntityCoordinates(target, Vector2.Zero));
                    steering.Range = range * 0.9f;
                    steering.Flags = PathFlags.Interact | PathFlags.Smashing;
                    steering.CurrentPath = new(result.Path);
                    steering.Status = SteeringStatus.Moving;
                    return;
                }
            }
            else
            {
                _ = task.Exception;
            }
            StopMovement(ent);
        }

        if (TryComp<NPCSteeringComponent>(ent, out var current) && current.Status == SteeringStatus.Moving)
            return;
        if (ent.Comp.Path != null || _timing.CurTime < ent.Comp.NextPathSearch)
            return;

        StopMovement(ent);
        ent.Comp.NextPathSearch = _timing.CurTime + TimeSpan.FromSeconds(1);
        ent.Comp.PathCancellation = new CancellationTokenSource();
        // Native melee steering can shortcut line of sight across space. Require a floor route first.
        ent.Comp.Path = _pathfinding.GetPath(ent, Transform(ent).Coordinates,
            new EntityCoordinates(target, Vector2.Zero), range * 0.9f, ent.Comp.PathCancellation.Token,
            flags: PathFlags.Interact | PathFlags.Smashing);
    }

    private void Retreat(Entity<RotRetaliationComponent> ent, EntityUid target)
    {
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        _combat.SetInCombatMode(ent, false);
        if (ent.Comp.Path is { IsCompleted: true } task)
        {
            CancelPath(ent);
            if (task.IsCompletedSuccessfully)
            {
#pragma warning disable RA0004
                var result = task.Result;
#pragma warning restore RA0004
                if (result.Result == PathResult.Path && result.Path.Count > 0
                    && result.Path[^1].Coordinates is var point && point.IsValid(EntityManager)
                    && _escape.IsFurtherFromThreat(ent, target, point))
                {
                    var origin = _transform.GetMapCoordinates(ent);
                    var offset = _transform.ToMapCoordinates(point).Position - origin.Position;
                    _steering.PrunePath(ent, origin, offset, result.Path);
                    ent.Comp.Direction = Vector2.Normalize(offset);
                    ent.Comp.Destination = point;
                    var steering = _steering.Register(ent, point);
                    steering.Range = 0.6f;
                    steering.InRangeMaxSpeed = 0.1f;
                    steering.Status = SteeringStatus.Moving;
                    steering.Flags = PathFlags.Interact | PathFlags.Smashing;
                    steering.CurrentPath = new(result.Path);
                }
            }
            else
            {
                _ = task.Exception;
            }
        }

        var hasRoute = ent.Comp.Destination is { } destination && destination.IsValid(EntityManager)
            && Transform(ent).Coordinates.TryDistance(EntityManager, destination, out var distance)
            && distance > ent.Comp.AdvanceRange && _escape.IsFurtherFromThreat(ent, target, destination)
            && TryComp<NPCSteeringComponent>(ent, out var current) && current.Status == SteeringStatus.Moving;
        if (hasRoute || ent.Comp.Path != null || _timing.CurTime < ent.Comp.NextPathSearch)
            return;
        ent.Comp.NextPathSearch = _timing.CurTime + TimeSpan.FromSeconds(1);
        ent.Comp.PathCancellation = new CancellationTokenSource();
        ent.Comp.Path = _escape.FindPath(ent, target, ent.Comp.EscapeRange, ent.Comp.AdvanceRange,
            ent.Comp.Direction, ent.Comp.PathCancellation.Token, preferCover: true);
    }

    private void OnMobState(Entity<RotRetaliationComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Clear(ent);
    }

    private void OnPlayerAttached(Entity<RotRetaliationComponent> ent, ref PlayerAttachedEvent args) => Clear(ent);

    private void OnShutdown(Entity<RotRetaliationComponent> ent, ref ComponentShutdown args) => Clear(ent);

    private void Clear(Entity<RotRetaliationComponent> ent)
    {
        StopMovement(ent);
        ent.Comp.Target = null;
        ent.Comp.Retreating = false;
        ent.Comp.Direction = Vector2.Zero;
    }

    public void StopMovement(Entity<RotRetaliationComponent> ent)
    {
        CancelPath(ent);
        ent.Comp.Destination = null;
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
    }

    private void CancelPath(Entity<RotRetaliationComponent> ent)
    {
        ent.Comp.PathCancellation?.Cancel();
        ent.Comp.PathCancellation?.Dispose();
        ent.Comp.PathCancellation = null;
        ent.Comp.Path = null;
    }
}
