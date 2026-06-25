using System.Numerics;
using Content.Server.Pinpointer;
using Content.Shared._Exodus.Asakim;
using Content.Shared.Interaction;
using Content.Shared.Pinpointer;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Asakim;

public sealed class AsakimBrainPinpointerSystem : EntitySystem
{
    [Dependency] private PinpointerSystem _pinpointer = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AsakimBrainPinpointerComponent, ActivateInWorldEvent>(OnActivate, after: [typeof(PinpointerSystem)]);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<AsakimBrainPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var asakim, out var pinpointer))
        {
            if (!pinpointer.IsActive || curTime < asakim.NextUpdate)
                continue;

            UpdateTarget((uid, asakim, pinpointer), curTime);
        }
    }

    private void OnActivate(Entity<AsakimBrainPinpointerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || !TryComp<PinpointerComponent>(ent, out var pinpointer) || !pinpointer.IsActive)
            return;

        var curTime = _timing.CurTime;
        if (curTime < ent.Comp.NextUpdate)
            return;

        UpdateTarget((ent.Owner, ent.Comp, pinpointer), curTime);
    }

    private void UpdateTarget(Entity<AsakimBrainPinpointerComponent, PinpointerComponent> ent, TimeSpan curTime)
    {
        ent.Comp1.NextUpdate = curTime + ent.Comp1.UpdateInterval;
        _pinpointer.SetTarget(ent.Owner, FindNearestAsakimBrain(ent.Owner, Transform(ent.Owner)), ent.Comp2);
    }

    private EntityUid? FindNearestAsakimBrain(EntityUid source, TransformComponent sourceTransform)
    {
        var mapId = sourceTransform.MapID;
        var sourcePosition = _transform.GetWorldPosition(sourceTransform);
        var nearestDistance = float.MaxValue;
        EntityUid? nearest = null;

        var query = EntityQueryEnumerator<AsakimBrainComponent>();
        while (query.MoveNext(out var brainUid, out _))
        {
            var brainTransform = Transform(brainUid);
            if (brainUid == source || brainTransform.MapID != mapId)
                continue;

            var distance = (_transform.GetWorldPosition(brainTransform) - sourcePosition).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = brainUid;
        }

        return nearest;
    }
}
