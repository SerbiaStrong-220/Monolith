using System.Numerics;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>Threat selection and provisioning policy; movement, cover and melee reuse the colony's defender systems.</summary>
public sealed partial class RotNesterSystem : EntitySystem
{
    [Dependency] private RotColonySiteSystem _sites = default!;
    [Dependency] private RotDefenderSystem _navigation = default!;
    [Dependency] private RotRetaliationSystem _retaliation = default!;
    [Dependency] private RotIntelligentSystem _colony = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<Entity<RotColonySiteComponent, TransformComponent>> _homeSites = [];
    private readonly HashSet<EntityUid> _observers = [];
    private EntityQuery<RotNesterComponent> _nesters;
    private float _observationRange;

    private readonly HashSet<Entity<MobStateComponent>> _candidates = [];
    private readonly List<EntityUid> _expired = [];
    private readonly List<(EntityUid Uid, float Distance)> _enemies = [];
    private EntityQuery<HandsComponent> _hands;
    private EntityQuery<RotCreatureComponent> _rot;

    public override void Initialize()
    {
        base.Initialize();
        _nesters = GetEntityQuery<RotNesterComponent>();
        _hands = GetEntityQuery<HandsComponent>();
        _rot = GetEntityQuery<RotCreatureComponent>();
        SubscribeLocalEvent<RotNesterComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<RotNesterComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RotNesterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RotNesterComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RotNesterComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<RotNesterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RotCombatObservedEvent>(OnObservedAttack);
        InitializeFeeding();
    }

    private void OnInit(Entity<RotNesterComponent> ent, ref MapInitEvent args)
    {
        _observationRange = MathF.Max(_observationRange, ent.Comp.SearchRange);
        ent.Comp.LastReserve = ent.Comp.NextReserve = _timing.CurTime;
        ent.Comp.Origin = Transform(ent).Coordinates;
        ent.Comp.Aggressors.Clear();
        ent.Comp.RejectedHomes.Clear();
    }

    private void OnMobState(Entity<RotNesterComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
        {
            Stop(ent);
            ent.Comp.Aggressors.Clear();
            ent.Comp.RejectedHomes.Clear();
        }
    }

    private void OnShutdown(Entity<RotNesterComponent> ent, ref ComponentShutdown args) => Stop(ent);
    private void OnAttached(Entity<RotNesterComponent> ent, ref PlayerAttachedEvent args) => Stop(ent);

    private void OnExamined(Entity<RotNesterComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("rot-nester-reserve", ("percent",
            Math.Clamp((int)(ent.Comp.Reserve / ent.Comp.FillDuration * 100), 0, 100))));
    }

    private void OnObservedAttack(ref RotCombatObservedEvent args) => ObserveAttack(args.Attacker);

    private void ObserveAttack(EntityUid source)
    {
        if (!IsEnemy(source))
            return;
        var origin = _transform.GetMapCoordinates(source);
        if (_observationRange <= 0)
            return;
        _observers.Clear();
        // The untyped lookup always uses spatial trees, even when observers are sparse.
        _lookup.GetEntitiesInRange(source, _observationRange, _observers, LookupFlags.Uncontained);
        foreach (var uid in _observers)
        {
            if (!_nesters.TryComp(uid, out var nester) || Paused(uid))
                continue;
            var point = _transform.GetMapCoordinates(uid);
            if (point.MapId != origin.MapId || Vector2.DistanceSquared(point.Position, origin.Position) > nester.SearchRange * nester.SearchRange
                || !_whitelist.IsValid(nester.Targets, source) || !_mobs.IsAlive(uid) || _containers.IsEntityInContainer(uid)
                || !_interaction.InRangeUnobstructed(uid, source, nester.SearchRange, collisionMask: CollisionGroup.Opaque))
                continue;
            Remember((uid, nester), source);
        }
    }

    private void OnDamaged(Entity<RotNesterComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !_mobs.IsAlive(ent))
            return;
        CancelProvision(ent);
        var source = args.Origin;
        if (source == null && TryComp<ProjectileComponent>(args.Tool, out var projectile))
            source = projectile.Shooter;
        if (source is { } attacker && IsEnemy(attacker))
            Remember(ent, attacker);
    }

    private void Remember(Entity<RotNesterComponent> ent, EntityUid attacker)
    {
        ent.Comp.Aggressors[attacker] = _timing.CurTime + ent.Comp.ThreatMemory;
        ent.Comp.Threat = attacker;
        ent.Comp.FleeUntil = _timing.CurTime + ent.Comp.ReactionHysteresis;
        ent.Comp.NextThink = TimeSpan.Zero;
    }

    private bool IsEnemy(EntityUid uid) => !TerminatingOrDeleted(uid) && !_rot.HasComp(uid)
        && (_mobs.IsAlive(uid) || _mobs.IsCritical(uid)) && !_containers.IsEntityInContainer(uid);

    public bool IsThreat(Entity<RotNesterComponent> ent, EntityUid uid)
    {
        if (!IsEnemy(uid))
            return false;
        if (ent.Comp.Aggressors.TryGetValue(uid, out var until) && until > _timing.CurTime)
            return true;
        if (!_hands.TryComp(uid, out var hands))
            return false;
        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is { } item && _whitelist.IsValid(ent.Comp.Weapons, item))
                return true;
        }
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _observationRange = 0;
        var query = EntityQueryEnumerator<RotNesterComponent>();
        while (query.MoveNext(out var uid, out var nester))
        {
            _observationRange = MathF.Max(_observationRange, nester.SearchRange);
            if (now < nester.NextReserve || !_mobs.IsAlive(uid))
                continue;
            nester.NextReserve = now + TimeSpan.FromSeconds(1);
            var elapsed = now - nester.LastReserve;
            nester.LastReserve = now;
            if (!_containers.IsEntityInContainer(uid))
                nester.Reserve = TimeSpan.FromTicks(Math.Min(nester.FillDuration.Ticks, nester.Reserve.Ticks + elapsed.Ticks));
            _expired.Clear();
            foreach (var (enemy, until) in nester.Aggressors)
            {
                if (until <= now || !IsEnemy(enemy))
                    _expired.Add(enemy);
            }
            foreach (var enemy in _expired)
                nester.Aggressors.Remove(enemy);
            _expired.Clear();
            foreach (var (home, until) in nester.RejectedHomes)
            {
                if (until <= now || TerminatingOrDeleted(home))
                    _expired.Add(home);
            }
            foreach (var home in _expired)
                nester.RejectedHomes.Remove(home);
            // Navigation memory is irrelevant to this defender's temporary threat policy.
            if (TryComp<RotDefenderComponent>(uid, out var navigation))
            {
                if (now >= navigation.RetryEnemiesAt)
                    navigation.UnreachableEnemies.Clear();
            }
        }
    }

    public void Think(Entity<RotNesterComponent> ent)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextThink || TerminatingOrDeleted(ent) || !_mobs.IsAlive(ent)
            || HasComp<ActorComponent>(ent) || _containers.IsEntityInContainer(ent)
            || !TryComp<RotDefenderComponent>(ent, out var navigation))
            return;
        ent.Comp.NextThink = now + ent.Comp.ThinkInterval;
        var brain = new Entity<RotDefenderComponent>(ent, navigation);
        _candidates.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), ent.Comp.SearchRange, _candidates);
        EntityUid? fear = null;
        var closest = float.MaxValue;
        _enemies.Clear();
        navigation.Enemies.Clear();
        foreach (var (uid, _) in _candidates)
        {
            if (!IsEnemy(uid) || !_whitelist.IsValid(ent.Comp.Targets, uid)
                || !_interaction.InRangeUnobstructed(ent.Owner, uid, ent.Comp.SearchRange, collisionMask: CollisionGroup.Opaque))
                continue;
            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(ent), _transform.GetWorldPosition(uid));
            if (IsThreat(ent, uid))
            {
                navigation.Enemies.Add(uid);
                if (distance < closest)
                {
                    fear = uid;
                    closest = distance;
                }
            }
            else if (WithinPursuitLeash(ent, uid))
                _enemies.Add((uid, distance));
        }
        var seenThreat = fear != null;
        if (fear == null && ent.Comp.Threat is { } remembered && IsEnemy(remembered)
            && (now < ent.Comp.FleeUntil || ent.Comp.Aggressors.ContainsKey(remembered)
                && _interaction.InRangeUnobstructed(ent.Owner, remembered, 64, collisionMask: CollisionGroup.Opaque)))
            fear = remembered;
        if (fear is { } avoid)
        {
            CancelProvision(ent);
            if (ent.Comp.Retaliating && TryComp<RotRetaliationComponent>(ent, out var suspended))
                _retaliation.StopMovement((ent, suspended));
            ent.Comp.Retaliating = false;
            ent.Comp.Threat = avoid;
            if (seenThreat)
                ent.Comp.FleeUntil = now + ent.Comp.ReactionHysteresis;
            _navigation.AvoidDefenderThreat(brain, avoid, true);
            return;
        }
        if (TryComp<RotRetaliationComponent>(ent, out var retaliation) && retaliation.Target != null)
        {
            CancelProvision(ent);
            if (!ent.Comp.Retaliating)
            {
                _navigation.Stop(brain);
                _retaliation.Begin(ent);
                ent.Comp.Retaliating = true;
            }
            if (_retaliation.Think((ent, retaliation)))
                return;
        }
        ent.Comp.Retaliating = false;
        if (navigation.EnvironmentalDamage && _navigation.AvoidDefenderThreat(brain, ent.Comp.Threat, false))
        {
            CancelProvision(ent);
            return;
        }
        _enemies.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        EntityUid? unreachable = null;
        foreach (var (enemy, _) in _enemies)
        {
            if (!_navigation.PursueDefenderTarget(brain, enemy))
            {
                unreachable ??= enemy;
                continue;
            }
            CancelProvision(ent);
            return;
        }
        if (_navigation.AvoidDefenderThreat(brain, unreachable ?? ent.Comp.Threat, unreachable != null))
        {
            CancelProvision(ent);
            return;
        }
        if (TryProvision(ent, brain))
            return;
        if (_colony.TryGetRally(ent, out var rally))
        {
            _navigation.MoveDefender(brain, rally, 1.5f);
            return;
        }
        GoHome(ent, brain);
    }

    private void GoHome(Entity<RotNesterComponent> ent, Entity<RotDefenderComponent> brain)
    {
        if (ent.Comp.Home is { } old && (TerminatingOrDeleted(old)
            || HasComp<RotIntelligentComponent>(old) && !_colony.IsLivingCore(old)
            || Transform(old).GridUid != Transform(ent).GridUid
            || TryComp<NPCSteeringComponent>(ent, out var steering) && steering.Status == SteeringStatus.NoPath))
        {
            ent.Comp.RejectedHomes[old] = _timing.CurTime + ent.Comp.HomeRetryDelay;
            ent.Comp.Home = null;
            ent.Comp.NextHomeSearch = TimeSpan.Zero;
        }
        if (_timing.CurTime >= ent.Comp.NextHomeSearch)
        {
            ent.Comp.NextHomeSearch = _timing.CurTime + ent.Comp.HomeSearchInterval;
            if (TryComp<RotColonyMemberComponent>(ent, out var membership) && membership.Core is { } core
                && _colony.IsLivingCore(core) && !ent.Comp.RejectedHomes.ContainsKey(core) && Transform(core).GridUid == Transform(ent).GridUid)
                ent.Comp.Home = core;
            if (ent.Comp.Home == null)
            {
                var closest = float.MaxValue;
                _sites.GetSites(ent, _homeSites);
                foreach (var (uid, _, xform) in _homeSites)
                {
                    if (ent.Comp.RejectedHomes.ContainsKey(uid) || xform.GridUid == null || xform.GridUid != Transform(ent).GridUid
                        || TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid))
                        continue;
                    var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(ent), _transform.GetWorldPosition(uid));
                    if (distance >= closest)
                        continue;
                    closest = distance;
                    ent.Comp.Home = uid;
                }
            }
        }
        if (ent.Comp.Home is { } home)
            _navigation.MoveDefender(brain, new EntityCoordinates(home, Vector2.Zero), ent.Comp.HomeRange);
        else
            _steering.Unregister(ent);
    }

    private bool WithinPursuitLeash(Entity<RotNesterComponent> ent, EntityUid target)
    {
        var origin = ent.Comp.Origin;
        if (TryComp<RotColonyMemberComponent>(ent, out var membership) && _colony.IsLivingCore(membership.Core))
            origin = Transform(membership.Core!.Value).Coordinates;
        else if (ent.Comp.Home is { } home && !TerminatingOrDeleted(home))
            origin = Transform(home).Coordinates;
        return origin.IsValid(EntityManager) && origin.TryDistance(EntityManager, Transform(target).Coordinates, out var distance)
            && distance <= ent.Comp.PursuitLeash;
    }

    public void Stop(Entity<RotNesterComponent> ent)
    {
        CancelProvision(ent);
        if (TryComp<RotDefenderComponent>(ent, out var navigation))
            _navigation.Stop((ent, navigation));
        if (TryComp<RotRetaliationComponent>(ent, out var retaliation))
            _retaliation.StopMovement((ent, retaliation));
        ent.Comp.Retaliating = false;
    }
}
