using Content.Server.Explosion.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Implants;

public sealed class DelayedTriggerOnMobstateChangeSystem : EntitySystem
{
    private const float UpdateTimer = 5f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;

    private float _updateTimer = 0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DelayedTriggerOnMobstateChangeComponent, ImplantImplantedEvent>(OnImplanted);
        SubscribeLocalEvent<DelayedTriggerOnMobstateChangeComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<DelayedTriggerOnMobstateChangeComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateRelay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;
        if (_updateTimer < UpdateTimer)
            return;
        _updateTimer = 0f;

        var query = EntityQueryEnumerator<DelayedTriggerOnMobstateChangeComponent, SubdermalImplantComponent>();
        while (query.MoveNext(out var uid, out var delayed, out var implant))
        {
            if (delayed.TriggerAt == TimeSpan.Zero || _timing.CurTime < delayed.TriggerAt)
                continue;

            if (implant.ImplantedEntity is not { } implanted || Deleted(implanted))
                continue;

            ResetDelay((uid, delayed));

            if (_mobState.IsDead(implanted))
                _trigger.Trigger(uid, implanted);
        }
    }

    private void OnImplanted(Entity<DelayedTriggerOnMobstateChangeComponent> ent, ref ImplantImplantedEvent args)
    {
        if (args.Implanted is not { } implanted || !_mobState.IsDead(implanted))
            return;

        StartDelay(ent);
    }

    private void OnRemoved(Entity<DelayedTriggerOnMobstateChangeComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        ResetDelay(ent);
    }

    private void OnMobStateRelay(Entity<DelayedTriggerOnMobstateChangeComponent> ent, ref ImplantRelayEvent<MobStateChangedEvent> args)
    {
        if (args.Event.NewMobState == MobState.Dead)
        {
            StartDelay(ent);
            return;
        }

        if (args.Event.OldMobState == MobState.Dead)
            ResetDelay(ent);
    }

    private void StartDelay(Entity<DelayedTriggerOnMobstateChangeComponent> ent)
    {
        ent.Comp.TriggerAt = _timing.CurTime + ent.Comp.Delay;
    }

    private void ResetDelay(Entity<DelayedTriggerOnMobstateChangeComponent> ent)
    {
        ent.Comp.TriggerAt = TimeSpan.Zero;
    }
}
