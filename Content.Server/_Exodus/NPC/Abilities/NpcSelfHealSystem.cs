using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed class NpcSelfHealSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private HealOverTimeSystem _healOverTime = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcSelfHealComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcSelfHealComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NpcSelfHealComponent, NpcSelfHealActionEvent>(OnSelfHeal);
        SubscribeLocalEvent<NpcSelfHealComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
        SubscribeLocalEvent<NpcSelfHealComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnMapInit(Entity<NpcSelfHealComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    private void OnSelfHeal(Entity<NpcSelfHealComponent> ent, ref NpcSelfHealActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var now = _timing.CurTime;
        var comp = ent.Comp;

        StopSound(comp);

        comp.EndTime = now + comp.Duration;
        comp.NextHeal = now + comp.HealInterval;
        comp.Healed = FixedPoint2.Zero;

        var ticks = (int) (comp.Duration.TotalSeconds / comp.HealInterval.TotalSeconds);
        comp.PerTick = ticks > 0 ? comp.TotalHeal / ticks : comp.TotalHeal;

        var sound = _audio.PlayPvs(comp.Sound, ent, AudioParams.Default.WithLoop(true));
        comp.SoundEntity = sound?.Entity;

        _movement.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<NpcSelfHealComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.EndTime == null)
                continue;

            if (now >= comp.EndTime)
            {
                EndBuff((uid, comp));
                continue;
            }

            if (now < comp.NextHeal || comp.Healed >= comp.TotalHeal)
                continue;

            comp.NextHeal += comp.HealInterval;
            var amount = FixedPoint2.Min(comp.PerTick, comp.TotalHeal - comp.Healed);
            if (amount > FixedPoint2.Zero)
            {
                _healOverTime.Heal(uid, amount);
                comp.Healed += amount;
            }
        }
    }

    private void EndBuff(Entity<NpcSelfHealComponent> ent)
    {
        StopSound(ent.Comp);
        ent.Comp.EndTime = null;
        _movement.RefreshMovementSpeedModifiers(ent); // restore speed
    }

    private void OnShutdown(Entity<NpcSelfHealComponent> ent, ref ComponentShutdown args)
    {
        StopSound(ent.Comp);
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void StopSound(NpcSelfHealComponent comp)
    {
        comp.SoundEntity = _audio.Stop(comp.SoundEntity);
    }

    private void OnRefreshMovespeed(Entity<NpcSelfHealComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.EndTime == null)
            return;

        args.ModifySpeed(ent.Comp.WalkSpeedModifier, ent.Comp.SprintSpeedModifier);
    }

    private void OnShotAttempted(Entity<NpcSelfHealComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.EndTime != null)
            args.Cancel(); // can't shoot while active
    }
}
