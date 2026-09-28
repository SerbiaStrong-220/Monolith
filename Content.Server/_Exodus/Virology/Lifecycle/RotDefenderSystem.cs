using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Shared pursuit, escape and obstruction handling, independent of feeding and target selection.</summary>
public sealed partial class RotDefenderSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private IGameTiming _timing = default!;
    private EntityQuery<RotCreatureComponent> _rotQuery;

    public override void Initialize()
    {
        base.Initialize();
        _rotQuery = GetEntityQuery<RotCreatureComponent>();
        SubscribeLocalEvent<RotDefenderComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<RotDefenderComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<RotDefenderComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RotDefenderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RotDefenderComponent, PlayerAttachedEvent>(OnAttached);
    }

    private void OnInit(Entity<RotDefenderComponent> ent, ref MapInitEvent args) => ResetProgress(ent);

    private void OnDamaged(Entity<RotDefenderComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !_mobs.IsAlive(ent))
            return;
        if (args.Origin is { } attacker && IsEnemy(attacker))
        {
            ent.Comp.Enemies.Add(attacker);
            ent.Comp.Threat = attacker;
        }
        if (HasComp<ActorComponent>(ent))
            return;
        ent.Comp.EnvironmentalDamage |= args.Origin is not { } source || !IsEnemy(source);
        ent.Comp.DamagePending = true;
    }

    private void OnMobState(Entity<RotDefenderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;
        Stop(ent);
        if (args.NewMobState == MobState.Dead)
            ent.Comp.Enemies.Clear();
    }

    private void OnShutdown(Entity<RotDefenderComponent> ent, ref ComponentShutdown args)
    {
        Stop(ent);
        ent.Comp.Enemies.Clear();
    }

    private void OnAttached(Entity<RotDefenderComponent> ent, ref PlayerAttachedEvent args) => Stop(ent);

    public bool IsEnemy(EntityUid uid) => !TerminatingOrDeleted(uid) && !_rotQuery.HasComp(uid)
        && (_mobs.IsAlive(uid) || _mobs.IsCritical(uid));

    public bool IsRejectedEnemy(Entity<RotDefenderComponent> ent, EntityUid target) =>
        ent.Comp.UnreachableEnemies.Contains(target) && !_interaction.InRangeUnobstructed(ent.Owner, target, 1.2f);

    public bool CanPursue(Entity<RotDefenderComponent> ent, EntityUid target)
    {
        if (!IsEnemy(target))
            return false;
        if (_interaction.InRangeUnobstructed(ent.Owner, target, 1.2f))
            return true;
        var transform = Transform(target);
        if (transform.GridUid is not { } grid || transform.MapUid != Transform(ent).MapUid
            || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(grid, out var mapGrid))
            return false;
        return !_turf.IsSpace(_map.GetTileRef(grid, mapGrid, _map.TileIndicesFor(grid, mapGrid, transform.Coordinates)));
    }

    public void RejectEnemy(Entity<RotDefenderComponent> ent, EntityUid target)
    {
        ent.Comp.UnreachableEnemies.Add(target);
        ent.Comp.Threat ??= target;
        ent.Comp.Target = null;
        ent.Comp.RetryEnemiesAt = _timing.CurTime + TimeSpan.FromSeconds(15);
        ResetProgress(ent);
    }

    public void Fight(Entity<RotDefenderComponent> ent)
    {
        if (ent.Comp.Target is not { } target || !IsEnemy(target))
            return;
        ent.Comp.Threat = target;
        var obstacle = target;
        if (_containers.TryGetOuterContainer(target, Transform(target), out var container))
            obstacle = container.Owner;
        EnsureComp<NPCMeleeCombatComponent>(ent).Target = obstacle;
        _combat.SetInCombatMode(ent, true);
        Move(ent, new EntityCoordinates(obstacle, Vector2.Zero), 0.8f);
    }

    public void Move(Entity<RotDefenderComponent> ent, EntityCoordinates destination, float range)
    {
        if (!ent.Comp.MovementRequested)
            ResetProgress(ent);
        ent.Comp.MovementRequested = true;
        if (TryComp<NPCSteeringComponent>(ent, out var old) && old.Status == SteeringStatus.NoPath)
            _steering.Unregister(ent);
        var steering = _steering.Register(ent, destination);
        steering.Range = range;
    }

    public void Stop(Entity<RotDefenderComponent> ent)
    {
        CancelRoute(ent);
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        ent.Comp.Target = null;
        ent.Comp.DetouredTarget = null;
        ent.Comp.MovementRequested = false;
        ent.Comp.UnreachableEnemies.Clear();
        ent.Comp.Threat = null;
        ent.Comp.DamagePending = ent.Comp.EnvironmentalDamage = false;
        ResetProgress(ent);
    }
}
