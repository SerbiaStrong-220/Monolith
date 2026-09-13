using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Body.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Scavenges corpses, avoids bystanders and remembers attackers until the fight is over.</summary>
public sealed partial class RotSatedSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private IGameTiming _timing = default!;
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
    }

    private void OnDamaged(Entity<RotSatedComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _mobs.IsDead(ent) || args.Origin is not { } attacker
            || !IsEnemy(attacker) || HasComp<ActorComponent>(ent))
            return;

        CancelConsumption(ent);
        ent.Comp.BirthRequested = ent.Comp.PendingLarvae > 0;
        CancelRoute(ent);
        ent.Comp.Enemies.Add(attacker);
        // Retain the current conscious opponent, even if another attacker deals more damage.
        if (ent.Comp.Target is not { } target || !IsEnemy(target) || _mobs.IsCritical(target))
            ent.Comp.Target = attacker;
        ent.Comp.NextThink = _timing.CurTime;
        Fight(ent);
    }

    private void OnMobState(Entity<RotSatedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Stop(ent);
    }

    private void OnShutdown(Entity<RotSatedComponent> ent, ref ComponentShutdown args) => Stop(ent);

    private void OnPlayerAttached(Entity<RotSatedComponent> ent, ref PlayerAttachedEvent args) => Stop(ent);

    private bool IsEnemy(EntityUid uid) => !TerminatingOrDeleted(uid) && !_rotQuery.HasComp(uid)
        && (_mobs.IsAlive(uid) || _mobs.IsCritical(uid));

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Spawning an HTN mob inside an operator would invalidate the native NPC enumeration.
        var query = EntityQueryEnumerator<RotSatedComponent>();
        while (query.MoveNext(out var uid, out var sated))
        {
            if (!sated.BirthRequested)
                continue;
            sated.BirthRequested = false;
            SpawnLarvae((uid, sated));
        }
    }

    public void Think(Entity<RotSatedComponent> ent)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextThink || TerminatingOrDeleted(ent) || _mobs.IsDead(ent)
            || HasComp<ActorComponent>(ent) || _containers.IsEntityInContainer(ent))
            return;
        ent.Comp.NextThink = now + ent.Comp.ThinkInterval;

        if (UpdateConsumption(ent))
            return;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), ent.Comp.ThreatRange, _nearby);
        if (ent.Comp.Enemies.Count > 0)
        {
            SelectEnemy(ent);
            if (ent.Comp.Target != null)
            {
                Fight(ent);
                return;
            }
        }

        RemComp<NPCMeleeCombatComponent>(ent);
        EntityUid? threat = null;
        var nearest = float.MaxValue;
        foreach (var (uid, _) in _nearby)
        {
            if (!IsEnemy(uid) || _mobs.IsCritical(uid) || _containers.IsEntityInContainer(uid)
                || !Transform(ent).Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance)
                || distance >= nearest || !_interaction.InRangeUnobstructed(ent.Owner, uid, ent.Comp.ThreatRange))
                continue;
            nearest = distance;
            threat = uid;
        }
        if (threat is { } avoid && !ent.Comp.Escaping)
        {
            ReleaseCorpse(ent);
            BeginRoute(ent, avoid);
            return;
        }

        if (ent.Comp.Escaping && UpdateRoute(ent))
            return;

        if (now < ent.Comp.RestUntil)
            return;

        if (ent.Comp.Corpse is { } old && (!CanConsume(ent, old)
            || TryComp<NPCSteeringComponent>(ent, out var oldSteering) && oldSteering.Status == SteeringStatus.NoPath))
        {
            ent.Comp.UnreachableCorpse = old;
            ent.Comp.CorpseRetryAt = now + TimeSpan.FromSeconds(30);
            ReleaseCorpse(ent);
        }
        if (ent.Comp.Corpse == null)
            FindCorpse(ent);
        if (ent.Comp.Corpse is not { } corpse)
        {
            if (UpdateRoute(ent))
                return;
            BeginRoute(ent, null);
            return;
        }

        CancelRoute(ent);
        Move(ent, new EntityCoordinates(corpse, Vector2.Zero), 0.7f);
        if (!_interaction.InRangeUnobstructed(ent.Owner, corpse, 1.2f))
            return;
        if (now < ent.Comp.NextStrip)
            return;
        ent.Comp.NextStrip = now + ent.Comp.StripInterval;
        if (TryStrip(ent, corpse))
            return;
        TryStartConsumption(ent, corpse);
    }

    private void SelectEnemy(Entity<RotSatedComponent> ent)
    {
        _removed.Clear();
        foreach (var enemy in ent.Comp.Enemies)
        {
            if (!IsEnemy(enemy))
                _removed.Add(enemy);
        }
        foreach (var enemy in _removed)
            ent.Comp.Enemies.Remove(enemy);

        if (ent.Comp.Target is { } current && ent.Comp.Enemies.Contains(current) && _mobs.IsAlive(current))
            return;

        var finishCurrent = ent.Comp.Target is { } wounded && ent.Comp.Enemies.Contains(wounded)
            && _mobs.IsCritical(wounded);
        if (!finishCurrent)
            ent.Comp.Target = null;
        var origin = _transform.GetMapCoordinates(ent);
        var best = float.MaxValue;
        var foundConscious = false;
        foreach (var enemy in ent.Comp.Enemies)
        {
            var position = _transform.GetMapCoordinates(enemy);
            if (position.MapId != origin.MapId)
                continue;
            var conscious = _mobs.IsAlive(enemy);
            var distance = Vector2.DistanceSquared(origin.Position, position.Position);
            // Leave a wounded victim only to deal with another immediate threat.
            if (finishCurrent && (!conscious || distance > ent.Comp.ThreatRange * ent.Comp.ThreatRange))
                continue;
            if (foundConscious && !conscious)
                continue;
            if (conscious == foundConscious && distance >= best)
                continue;
            foundConscious = conscious;
            best = distance;
            ent.Comp.Target = enemy;
        }
    }

    private void Fight(Entity<RotSatedComponent> ent)
    {
        if (ent.Comp.Target is not { } target || !IsEnemy(target))
            return;
        ReleaseCorpse(ent);
        var obstacle = target;
        if (_containers.TryGetOuterContainer(target, Transform(target), out var container))
            obstacle = container.Owner;
        EnsureComp<NPCMeleeCombatComponent>(ent).Target = obstacle;
        _combat.SetInCombatMode(ent, true);
        Move(ent, new EntityCoordinates(obstacle, Vector2.Zero), 0.8f);
    }

    private void Move(Entity<RotSatedComponent> ent, EntityCoordinates destination, float range)
    {
        if (TryComp<NPCSteeringComponent>(ent, out var old) && old.Status == SteeringStatus.NoPath)
            _steering.Unregister(ent);
        var steering = _steering.Register(ent, destination);
        steering.Range = range;
    }

    public void Stop(Entity<RotSatedComponent> ent)
    {
        CancelRoute(ent);
        CancelConsumption(ent);
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        ent.Comp.PendingLarvae = 0;
        ent.Comp.BirthRequested = false;
        ent.Comp.Target = null;
        ent.Comp.Enemies.Clear();
    }
}
