using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Body.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Scavenges corpses, remembers aggressors and retreats when opponents cannot be reached.</summary>
public sealed partial class RotSatedSystem : EntitySystem
{
    [Dependency] private RotDefenderSystem _navigation = default!;
    [Dependency] private RotGroundStrikeSystem _groundStrike = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Intelligent.RotIntelligentSystem _colony = default!;
    private readonly HashSet<Entity<BodyComponent>> _bodies = [];
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _nearby = [];
    private readonly List<EntityUid> _removed = [];
    private EntityQuery<RotCreatureComponent> _rotQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _rotQuery = GetEntityQuery<RotCreatureComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<RotSatedComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<RotSatedComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RotSatedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RotSatedComponent, PlayerAttachedEvent>(OnPlayerAttached);
        InitializeConsumption();
        InitializeActions();
        SubscribeLocalEvent<ExaminedEvent>(OnExamineEnemy);
        SubscribeLocalEvent<RotSatedComponent, RotDefenderRouteStartedEvent>(OnRouteStarted);
    }

    private void OnExamineEnemy(ExaminedEvent args)
    {
        if (HasComp<RotSatedComponent>(args.Examiner) && TryComp<RotDefenderComponent>(args.Examiner, out var defender)
            && defender.Enemies.Contains(args.Examined))
            args.PushMarkup(Loc.GetString("rot-sated-remembered-enemy"));
    }

    private void OnDamaged(Entity<RotSatedComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _mobs.IsDead(ent) || HasComp<ActorComponent>(ent))
            return;
        CancelConsumption(ent);
        ent.Comp.BirthRequested = ent.Comp.PendingLarvae > 0;
    }

    private void OnRouteStarted(Entity<RotSatedComponent> ent, ref RotDefenderRouteStartedEvent args)
    {
        if (args.Route == RotDefenderRoute.Detour && ent.Comp.Corpse is { } corpse)
        {
            ent.Comp.UnreachableCorpse = corpse;
            ent.Comp.CorpseRetryAt = _timing.CurTime + TimeSpan.FromSeconds(30);
        }
        ReleaseCorpse(ent);
    }

    private void OnMobState(Entity<RotSatedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;
        Stop(ent);
    }

    private void OnShutdown(Entity<RotSatedComponent> ent, ref ComponentShutdown args)
    {
        Stop(ent);
        RemoveActions(ent);
    }

    private void OnPlayerAttached(Entity<RotSatedComponent> ent, ref PlayerAttachedEvent args) => Stop(ent);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Spawning an HTN mob inside an operator would invalidate the native NPC enumeration.
        var query = EntityQueryEnumerator<RotSatedComponent>();
        while (query.MoveNext(out var uid, out var sated))
        {
            if (sated.PreparingConsumption && sated.StripDoAfter == null && sated.Corpse is { } corpse)
            {
                if (!TryPrepareConsumption((uid, sated), corpse))
                    CancelConsumption((uid, sated));
            }
            if (!TerminatingOrDeleted(uid) && !_mobs.IsDead(uid) && sated.Activity != RotSatedActivity.None
                && _timing.CurTime >= sated.NextActivityUpdate)
            {
                sated.NextActivityUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.2);
                UpdateConsumption((uid, sated));
            }
            if (!sated.BirthRequested)
                continue;
            sated.BirthRequested = false;
            SpawnLarvae((uid, sated));
        }
    }

    public void Think(Entity<RotSatedComponent> ent)
    {
        if (!TryComp<RotDefenderComponent>(ent, out var defender))
            return;
        var brain = new Entity<RotDefenderComponent>(ent, defender);
        var now = _timing.CurTime;
        if (now < ent.Comp.NextThink || TerminatingOrDeleted(ent) || _mobs.IsDead(ent)
            || HasComp<ActorComponent>(ent) || _containers.IsEntityInContainer(ent))
            return;
        ent.Comp.NextThink = now + defender.ThinkInterval;

        if (ent.Comp.Activity != RotSatedActivity.None || ent.Comp.StripDoAfter != null)
            return;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), defender.SearchRange, _nearby);
        var previousTarget = defender.Target;
        if (previousTarget is { } chasing && !defender.ClearingObstacle
            && defender.Route == RotDefenderRoute.None
            && TryComp<NPCSteeringComponent>(ent, out var failed) && failed.Status == SteeringStatus.NoPath
            && !_groundStrike.HasObstacle(ent, defender.StuckTileRadius))
            _navigation.RejectEnemy(brain, chasing);
        SelectEnemy(ent);

        var environmental = defender.EnvironmentalDamage;
        if (defender.Target is { } target && !environmental)
        {
            defender.DamagePending = defender.EnvironmentalDamage = false;
            ReleaseCorpse(ent);
            // Target selection does not own route transitions; the shared defender handles those.
            defender.Target = previousTarget;
            if (_navigation.PursueDefenderTarget(brain, target))
                return;
        }

        if (_navigation.AvoidDefenderThreat(brain, defender.Threat, false))
            return;
        if (defender.Threat is { } shooter && _navigation.IsEnemy(shooter)
            && _interaction.InRangeUnobstructed(ent.Owner, shooter, defender.EscapeRange,
                collisionMask: Content.Shared.Physics.CollisionGroup.Opaque))
        {
            _navigation.BeginRoute(brain, shooter, RotDefenderRoute.Cover);
            return;
        }

        EntityUid? threat = null;
        var nearest = float.MaxValue;
        foreach (var (uid, _) in _nearby)
        {
            if (!_navigation.IsEnemy(uid) || _mobs.IsCritical(uid) || _containers.IsEntityInContainer(uid)
                || !Transform(ent).Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance)
                || distance >= nearest || !_interaction.InRangeUnobstructed(ent.Owner, uid, ent.Comp.ThreatRange))
                continue;
            nearest = distance;
            threat = uid;
        }
        if (threat is { } avoid)
        {
            _navigation.BeginRoute(brain, avoid, RotDefenderRoute.Cover);
            return;
        }

        if (ent.Comp.Corpse is { } old && (!CanConsume(ent, old)
            || TryComp<NPCSteeringComponent>(ent, out var oldSteering) && oldSteering.Status == SteeringStatus.NoPath))
        {
            ent.Comp.UnreachableCorpse = old;
            ent.Comp.CorpseRetryAt = now + TimeSpan.FromSeconds(30);
            ReleaseCorpse(ent);
        }
        if (ent.Comp.Corpse == null && _colony.TryGetRally(ent, out var rally))
        {
            _navigation.MoveDefender(brain, rally, 1.5f);
            return;
        }
        if (ent.Comp.Corpse == null)
            FindCorpse(ent);
        if (ent.Comp.Corpse is not { } corpse)
        {
            if (_navigation.UpdateRoute(brain))
                return;
            _navigation.BeginRoute(brain, null, RotDefenderRoute.Wander);
            return;
        }

        _navigation.CancelRoute(brain);
        if (_navigation.HandleStuckMovement(brain, new EntityCoordinates(corpse, Vector2.Zero), 0.7f))
            return;
        _navigation.Move(brain, new EntityCoordinates(corpse, Vector2.Zero), 0.7f);
        if (!_interaction.InRangeUnobstructed(ent.Owner, corpse, 1.2f))
            return;
        TryPrepareConsumption(ent, corpse);
    }

    private void SelectEnemy(Entity<RotSatedComponent> ent)
    {
        if (!TryComp<RotDefenderComponent>(ent, out var defender))
            return;
        var brain = new Entity<RotDefenderComponent>(ent, defender);
        _removed.Clear();
        foreach (var enemy in defender.Enemies)
        {
            if (TerminatingOrDeleted(enemy))
                _removed.Add(enemy);
        }
        foreach (var enemy in _removed)
        {
            defender.Enemies.Remove(enemy);
            defender.UnreachableEnemies.Remove(enemy);
        }
        if (_timing.CurTime >= defender.RetryEnemiesAt)
        {
            defender.UnreachableEnemies.Clear();
            defender.RetryEnemiesAt = _timing.CurTime + TimeSpan.FromSeconds(15);
        }
        var old = defender.Target;
        defender.Target = null;
        var best = float.MaxValue;
        foreach (var (uid, _) in _nearby)
        {
            if (!defender.Enemies.Contains(uid) || !_navigation.CanPursue(brain, uid)
                || _navigation.IsRejectedEnemy(brain, uid)
                || uid != old && !_interaction.InRangeUnobstructed(ent.Owner, uid, defender.SearchRange))
                continue;
            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(ent), _transform.GetWorldPosition(uid));
            if (uid == old)
                distance *= 0.5f;
            if (_mobs.IsCritical(uid))
                distance += defender.SearchRange * defender.SearchRange;
            if (distance >= best)
                continue;
            best = distance;
            defender.Target = uid;
        }
        // Non-humanoid aggressors can also be pursued, without scanning all entities on the map.
        if (defender.Target == null && defender.Threat is { } threat && _navigation.CanPursue(brain, threat)
            && !_navigation.IsRejectedEnemy(brain, threat)
            && _interaction.InRangeUnobstructed(ent.Owner, threat, defender.SearchRange))
            defender.Target = threat;
        if (defender.Target is { } target)
            defender.Enemies.Add(target);
    }

    public void Stop(Entity<RotSatedComponent> ent)
    {
        if (TryComp<RotDefenderComponent>(ent, out var defender))
            _navigation.Stop((ent, defender));
        CancelConsumption(ent);
        if (TerminatingOrDeleted(ent) || _mobs.IsDead(ent))
            ent.Comp.PendingLarvae = 0;
        ent.Comp.BirthRequested = ent.Comp.PendingLarvae > 0;
    }
}
