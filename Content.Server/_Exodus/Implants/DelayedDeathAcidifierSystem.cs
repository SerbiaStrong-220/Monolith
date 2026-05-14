using Content.Server.Explosion.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Implants;

public sealed class DelayedDeathAcidifierSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DelayedDeathAcidifierComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DelayedDeathAcidifierComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<DelayedDeathAcidifierComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateRelay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<DelayedDeathAcidifierComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var uid, out var delayed, out var implant))
        {
            if (delayed.Triggered || delayed.TriggerAt is not { } triggerAt || curTime < triggerAt)
                continue;

            if (!TryGetDeadImplantedEntity(implant, out var implanted))
            {
                delayed.TriggerAt = null;
                continue;
            }

            delayed.Triggered = true;
            delayed.TriggerAt = null;
            _trigger.Trigger(uid, implanted);
        }
    }

    private void OnImplanted(Entity<DelayedDeathAcidifierComponent> ent, ref ImplantImplantedEvent args)
    {
        if (ent.Comp.Triggered || args.Implanted is not { } implanted)
            return;

        if (!TryComp<MobStateComponent>(implanted, out var mobState) || mobState.CurrentState != MobState.Dead)
            return;

        StartDelay(ent);
    }

    private void OnRemoved(Entity<DelayedDeathAcidifierComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        ent.Comp.TriggerAt = null;
    }

    private void OnMobStateRelay(Entity<DelayedDeathAcidifierComponent> ent, ref ImplantRelayEvent<MobStateChangedEvent> args)
    {
        if (ent.Comp.Triggered)
            return;

        if (args.Event.NewMobState == MobState.Dead)
        {
            StartDelay(ent);
            return;
        }

        if (args.Event.OldMobState == MobState.Dead)
            ent.Comp.TriggerAt = null;
    }

    private void StartDelay(Entity<DelayedDeathAcidifierComponent> ent)
    {
        ent.Comp.TriggerAt = _timing.CurTime + ent.Comp.Delay;
    }

    private bool TryGetDeadImplantedEntity(SubdermalImplantComponent implant, out EntityUid implanted)
    {
        implanted = EntityUid.Invalid;

        if (implant.ImplantedEntity is not { } target ||
            Deleted(target) ||
            !TryComp<MobStateComponent>(target, out var mobState) ||
            mobState.CurrentState != MobState.Dead)
        {
            return false;
        }

        implanted = target;
        return true;
    }
}
