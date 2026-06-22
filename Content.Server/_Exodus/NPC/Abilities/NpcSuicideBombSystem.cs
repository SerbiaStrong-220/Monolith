using Content.Server.Body.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed class NpcSuicideBombSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private const string BombRangeKey = "BombRange";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcSuicideBombComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcSuicideBombComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NpcSuicideBombComponent, NpcDetonateActionEvent>(OnDetonate);
        SubscribeLocalEvent<NpcSuicideBombComponent, DamageChangedEvent>(OnDamaged);
    }

    // Trigger action immediately if hp range.
    private void OnDamaged(Entity<NpcSuicideBombComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.HealthTrigger is not { } threshold || ent.Comp.Detonated || !args.DamageIncreased)
            return;

        if (!_thresholds.TryGetIncapPercentage(ent.Owner, args.Damageable.TotalDamage, out var incap)
            || 1f - (float) incap > threshold)
            return;

        ent.Comp.Detonated = true; // if mob survives, dont trigger again.
        if (ent.Comp.ActionEntity is { } action && _actions.TryGetActionData(action, out var data))
            _actions.PerformAction(ent.Owner, null, action, data, data.BaseEvent, _timing.CurTime, false);
    }

    private void OnMapInit(Entity<NpcSuicideBombComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _npc.SetBlackboard(ent, BombRangeKey, ent.Comp.DetonateRange);
    }

    private void OnShutdown(Entity<NpcSuicideBombComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnDetonate(Entity<NpcSuicideBombComponent> ent, ref NpcDetonateActionEvent args)
    {
        if (args.Handled || ent.Comp.Detonated)
            return;

        args.Handled = true;
        ent.Comp.Detonated = true;
        var comp = ent.Comp;
        var coords = Transform(ent).Coordinates;

        _audio.PlayPvs(comp.Sound, ent);

        if (comp.Effect is { } effect)
            Spawn(effect, coords);

        // Clone so spilling won't empty the prototype's solution.
        // This is safety for poorly configured prototypes, to avoid bugs.
        if (comp.Spill is { } spill)
            _puddle.TrySpillAt(coords, spill.Clone(), out _);

        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_xform.GetMapCoordinates(ent.Owner), comp.Radius))
        {
            if (mob.Owner == ent.Owner)
                continue;

            if (comp.Damage != null)
                _damageable.TryChangeDamage(mob, comp.Damage, origin: ent);

            if (comp.Bleed > 0f)
                _blood.TryModifyBleedAmount(mob, comp.Bleed);
        }

        if (comp.Explode)
            _explosion.QueueExplosion(ent.Owner, comp.ExplosionType, comp.ExplosionTotalIntensity, comp.ExplosionSlope, comp.ExplosionMaxTileIntensity);

        QueueDel(ent.Owner);
    }
}
