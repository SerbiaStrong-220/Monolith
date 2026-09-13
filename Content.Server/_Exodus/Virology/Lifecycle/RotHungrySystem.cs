using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Colony defenders use native steering and melee; target memory is independent of infection.</summary>
public sealed partial class RotHungrySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = [];
    private readonly HashSet<Entity<DestructibleComponent>> _obstacles = [];
    private readonly List<EntityUid> _removedPrey = [];
    private EntityQuery<RotCombatHistoryComponent> _historyQuery;
    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<RotCreatureComponent> _rotQuery;
    private EntityQuery<TransformComponent> _transformQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private TimeSpan _nextHealing;

    public override void Initialize()
    {
        base.Initialize();
        _historyQuery = GetEntityQuery<RotCombatHistoryComponent>();
        _handsQuery = GetEntityQuery<HandsComponent>();
        _rotQuery = GetEntityQuery<RotCreatureComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<RotHungryComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<RotHungryComponent, DamageChangedEvent>(OnHungryDamaged);
        SubscribeLocalEvent<RotHungryComponent, MeleeHitEvent>(OnHungryHit);
        SubscribeLocalEvent<HumanoidAppearanceComponent, MeleeAttackEvent>(OnAttack);
        SubscribeLocalEvent<HumanoidAppearanceComponent, DamageChangedEvent>(OnHumanoidDamaged);
        SubscribeLocalEvent<ExaminedEvent>(OnExamineThreat);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnShot);
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageDealt);
        SubscribeLocalEvent<DoorComponent, BeforeDoorOpenedEvent>(OnDoorOpening);
        InitializeFrenzy();
    }

    private void OnInit(Entity<RotHungryComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Home = ent.Comp.LastPosition = Transform(ent).Coordinates;
        ent.Comp.LastMoved = ent.Comp.NextThink = _timing.CurTime;
    }

    private void OnAttack(Entity<HumanoidAppearanceComponent> ent, ref MeleeAttackEvent args)
    {
        EnsureComp<RotCombatHistoryComponent>(ent).HasAttacked = true;
    }

    private void OnExamineThreat(ExaminedEvent args)
    {
        if (!TryComp<RotHungryComponent>(args.Examiner, out var hungry) || _mobs.IsDead(args.Examined))
            return;
        if (hungry.Prey.Contains(args.Examined) || IsThreat((args.Examiner, hungry), args.Examined))
            args.PushMarkup(Loc.GetString("rot-hungry-threat"));
    }

    private void OnShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        RememberAttacker(args.User);
    }

    private void OnDamageDealt(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } attacker && attacker != ent.Owner)
            RememberAttacker(attacker);
    }

    private void RememberAttacker(EntityUid attacker)
    {
        if (HasComp<HumanoidAppearanceComponent>(attacker))
            EnsureComp<RotCombatHistoryComponent>(attacker).HasAttacked = true;
    }

    private void OnHumanoidDamaged(Entity<HumanoidAppearanceComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin is { } attacker)
            EnsureComp<RotCombatHistoryComponent>(ent).LastAttacker = attacker;
    }

    private void OnHungryDamaged(Entity<RotHungryComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
        {
            if (ent.Comp.Retreating || ent.Comp.Frenzied)
                TryEndRetreat(ent);
            return;
        }

        ent.Comp.NextRegeneration = _timing.CurTime + ent.Comp.RegenerationDelay;
        if (args.Origin is { } attacker && HasComp<HumanoidAppearanceComponent>(attacker) && !_rotQuery.HasComp(attacker))
        {
            ent.Comp.Prey.Add(attacker);
            if (ent.Comp.Retreating)
                RememberPursuer(ent, attacker);
        }
    }

    private void OnHungryHit(Entity<RotHungryComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        // Keep the same target memory when a player controls the ghost role.
        foreach (var target in args.HitEntities)
        {
            if (IsThreat(ent, target))
                ent.Comp.Prey.Add(target);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextHealing)
            return;

        _nextHealing = now + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<RotHungryComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var hungry, out var damage))
        {
            if (_mobs.IsDead(uid) || now < hungry.NextRegeneration || damage.TotalDamage <= 0 || hungry.Regeneration <= 0)
                continue;

            var fraction = Math.Min(1f, hungry.Regeneration / damage.TotalDamage.Float());
            _damage.TryChangeDamage(uid, damage.Damage * -fraction, true, interruptsDoAfters: false);
        }
    }

    public bool IsThreat(Entity<RotHungryComponent> ent, EntityUid target)
    {
        if (!HasComp<HumanoidAppearanceComponent>(target) || _rotQuery.HasComp(target) || _mobs.IsDead(target))
            return false;

        if (_historyQuery.TryComp(target, out var history) && history.HasAttacked)
            return true;

        if (!_handsQuery.TryComp(target, out var hands))
            return false;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is { } item && _whitelist.IsValid(ent.Comp.Weapons, item))
                return true;
        }

        return false;
    }

    public void Think(Entity<RotHungryComponent> ent)
    {
        var now = _timing.CurTime;
        if (TerminatingOrDeleted(ent) || HasComp<ActorComponent>(ent) || _mobs.IsDead(ent)
            || _containers.IsEntityInContainer(ent))
            return;

        if (now < ent.Comp.NextThink)
        {
            // Service an already detected obstruction at its own cadence, without repeating threat searches.
            if (ent.Comp.ClearingObstacle && now >= ent.Comp.NextStuckAttack)
                ent.Comp.ClearingObstacle = HandleStuckMovement(ent);
            return;
        }

        ent.Comp.NextThink = now + ent.Comp.ThinkInterval;
        RememberNearbyThreats(ent);
        UpdateTarget(ent);
        UpdateRetreat(ent);

        if (UpdateDetour(ent))
            return;

        ent.Comp.ClearingObstacle = HandleStuckMovement(ent);
        if (ent.Comp.ClearingObstacle || ent.Comp.DetourPath != null)
            return;

        if (Transform(ent).GridUid == null)
        {
            ReturnHome(ent);
            return;
        }

        if (ent.Comp.Retreating)
        {
            Retreat(ent);
            return;
        }

        ent.Comp.Shelter = null;
        if (ent.Comp.Target is { } target)
        {
            DropCorpse(ent);
            // A locked prey may hide in a container or leave the map. Keep the prey, but only steer to reachable coordinates.
            if (_transform.GetMapCoordinates(target).MapId != _transform.GetMapCoordinates(ent).MapId)
            {
                Stop(ent);
                return;
            }

            var obstacle = target;
            if (_containers.TryGetOuterContainer(target, Transform(target), out var container))
                obstacle = container.Owner;
            var combat = EnsureComp<NPCMeleeCombatComponent>(ent);
            combat.Target = obstacle;
            _combat.SetInCombatMode(ent, true);
            Move(ent, new EntityCoordinates(obstacle, Vector2.Zero), 0.8f);
        }
        else if (!DeliverCorpse(ent))
        {
            ReturnHome(ent);
        }
    }

    private void RememberNearbyThreats(Entity<RotHungryComponent> ent)
    {
        _humanoids.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), ent.Comp.SearchRange, _humanoids);
        foreach (var (target, _) in _humanoids)
        {
            if (!_containers.IsEntityInContainer(target) && IsThreat(ent, target)
                && _interaction.InRangeUnobstructed(ent.Owner, target, ent.Comp.SearchRange))
                ent.Comp.Prey.Add(target);
        }
    }

    private void UpdateTarget(Entity<RotHungryComponent> ent)
    {
        _removedPrey.Clear();
        foreach (var prey in ent.Comp.Prey)
        {
            if (ent.Comp.Frenzied && ent.Comp.Target == prey && !TerminatingOrDeleted(prey) && !_mobs.IsDead(prey))
                continue;
            if (!InvalidPrey(prey))
                continue;
            if (!TerminatingOrDeleted(prey) && _mobs.IsDead(prey)
                && _historyQuery.TryComp(prey, out var history) && history.LastAttacker == ent.Owner)
                ent.Comp.Corpses.Add(prey);
            _removedPrey.Add(prey);
        }
        foreach (var prey in _removedPrey)
        {
            ent.Comp.Prey.Remove(prey);
            ent.Comp.PursuitPositions.Remove(prey);
        }
        var origin = _transform.GetMapCoordinates(ent);
        if (ent.Comp.Target is { } current && ent.Comp.Prey.Contains(current)
            && (ent.Comp.Frenzied || _transform.GetMapCoordinates(current).MapId == origin.MapId
                && !_containers.IsEntityInContainer(current)))
            return;

        ent.Comp.Target = null;
        var best = float.MaxValue;
        foreach (var target in ent.Comp.Prey)
        {
            var position = _transform.GetMapCoordinates(target);
            var distance = Vector2.DistanceSquared(origin.Position, position.Position);
            if (position.MapId != origin.MapId || distance >= best || _containers.IsEntityInContainer(target))
                continue;
            best = distance;
            ent.Comp.Target = target;
        }
    }

    private bool InvalidPrey(EntityUid target) => TerminatingOrDeleted(target) || _mobs.IsDead(target) || _rotQuery.HasComp(target);

    public void Stop(EntityUid uid)
    {
        RemComp<NPCMeleeCombatComponent>(uid);
        _steering.Unregister(uid);
        _combat.SetInCombatMode(uid, false);
        if (TryComp<RotHungryComponent>(uid, out var hungry))
        {
            CancelDetour((uid, hungry));
            hungry.Destination = null;
            hungry.ClearingObstacle = false;
        }
    }

    private void Move(Entity<RotHungryComponent> ent, EntityCoordinates destination, float range = 1f)
    {
        ent.Comp.Destination = destination;
        // A failed route may become usable after an obstacle is destroyed or a grid docks.
        if (TryComp<NPCSteeringComponent>(ent, out var previous) && previous.Status == SteeringStatus.NoPath)
            _steering.Unregister(ent);
        var steering = _steering.Register(ent, destination);
        steering.Range = range;
        steering.ObstacleRange = ent.Comp.ObstacleRange;
        steering.DirectMove = Transform(ent).GridUid is not { } grid || _transform.GetGrid(destination) != grid;
    }
}
