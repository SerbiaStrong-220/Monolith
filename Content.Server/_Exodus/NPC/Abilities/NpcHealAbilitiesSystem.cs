using System.Numerics;
using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.Actions;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed partial class NpcHealAbilitiesSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private HealOverTimeSystem _healOverTime = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcHealInjectionComponent, MapInitEvent>(OnInjectionMapInit);
        SubscribeLocalEvent<NpcHealInjectionComponent, ComponentShutdown>(OnInjectionShutdown);
        SubscribeLocalEvent<NpcHealInjectionComponent, NpcHealInjectionActionEvent>(OnInjection);

        SubscribeLocalEvent<NpcReviveComponent, MapInitEvent>(OnReviveMapInit);
        SubscribeLocalEvent<NpcReviveComponent, ComponentShutdown>(OnReviveShutdown);
        SubscribeLocalEvent<NpcReviveComponent, NpcReviveActionEvent>(OnRevive);

        SubscribeLocalEvent<NpcHealAuraComponent, MapInitEvent>(OnAuraMapInit);
        SubscribeLocalEvent<NpcHealAuraComponent, ComponentShutdown>(OnAuraShutdown);
        SubscribeLocalEvent<NpcHealAuraComponent, NpcHealAuraActionEvent>(OnAura);
    }

    private void OnInjectionMapInit(Entity<NpcHealInjectionComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    private void OnInjectionShutdown(Entity<NpcHealInjectionComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnInjection(Entity<NpcHealInjectionComponent> ent, ref NpcHealInjectionActionEvent args)
    {
        if (args.Handled || Deleted(args.Target))
            return;

        args.Handled = true;
        _audio.PlayPvs(ent.Comp.Sound, args.Target);
        _healOverTime.Apply(args.Target, ent.Comp.HealPerSecond, ent.Comp.Duration);
    }

    private void OnReviveMapInit(Entity<NpcReviveComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);

        _actions.AddAction(ent, ref ent.Comp.DragActionEntity, ent.Comp.DragAction);
        _actions.SetUseDelay(ent.Comp.DragActionEntity, ent.Comp.DragCooldown);
    }

    private void OnReviveShutdown(Entity<NpcReviveComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.DragActionEntity);
    }

    private void OnRevive(Entity<NpcReviveComponent> ent, ref NpcReviveActionEvent args)
    {
        if (args.Handled || Deleted(args.Target))
            return;

        args.Handled = true;
        _jitter.DoJitter(args.Target, ent.Comp.JitterDuration, refresh: true);
        _healOverTime.QueueHeal(args.Target, ent.Comp.Heal);
        _audio.PlayPvs(ent.Comp.Sound, args.Target);
    }

    private void OnAuraMapInit(Entity<NpcHealAuraComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    private void OnAuraShutdown(Entity<NpcHealAuraComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.VisualEntity is { } visual)
            QueueDel(visual);

        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnAura(Entity<NpcHealAuraComponent> ent, ref NpcHealAuraActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var comp = ent.Comp;
        var now = _timing.CurTime;

        comp.EndTime = now + comp.Duration;
        comp.NextTick = now + comp.Interval;

        // U may ask: wtf is this?
        // Healing aura should have its own sprite, but for now it uses pointlight
        // So we spawn separate entity attached to caster to avoid all bugs if caster already got own poitlight
        if (comp.VisualEntity is { } stale)
            QueueDel(stale);

        comp.VisualEntity = Spawn(comp.Effect, new EntityCoordinates(ent, Vector2.Zero));
        _light.SetRadius(comp.VisualEntity.Value, comp.Radius);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<NpcHealAuraComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.EndTime == null)
                continue;

            if (now >= comp.EndTime)
            {
                if (comp.VisualEntity is { } visual)
                    QueueDel(visual);

                comp.VisualEntity = null;
                comp.EndTime = null;
                continue;
            }

            if (now < comp.NextTick)
                continue;

            comp.NextTick += comp.Interval;
            if (comp.NextTick <= now)
                comp.NextTick = now + comp.Interval;
            HealAura((uid, comp));
        }
    }

    private void HealAura(Entity<NpcHealAuraComponent> ent)
    {
        var coords = _xform.GetMapCoordinates(ent.Owner);
        var amount = ent.Comp.HealPerSecond * (float) ent.Comp.Interval.TotalSeconds;

        foreach (var ally in _lookup.GetEntitiesInRange<MobStateComponent>(coords, ent.Comp.Radius))
        {
            if (!_faction.IsEntityFriendly(ent.Owner, ally.Owner))
                continue;

            _healOverTime.Heal(ally.Owner, amount);
        }
    }
}
