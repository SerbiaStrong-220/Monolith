using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Exodus.Tailed;

public sealed partial class TailedEntitySystem
{
    [Dependency] private MetaDataSystem _metadata = default!;

    private void InitializeTailJointRecovery()
    {
        UpdatesBefore.Add(typeof(SharedJointSystem));

        SubscribeLocalEvent<TailedEntityComponent, ComponentInit>(OnMapTrackingInit);
        SubscribeLocalEvent<TailedEntitySegmentComponent, ComponentInit>(OnMapTrackingInit);
        SubscribeLocalEvent<TailedEntityComponent, ComponentRemove>(OnMapTrackingRemove);
        SubscribeLocalEvent<TailedEntitySegmentComponent, ComponentRemove>(OnMapTrackingRemove);
        SubscribeLocalEvent<TailedEntityComponent, MetaFlagRemoveAttemptEvent>(OnMapTrackingFlagRemoveAttempt);
        SubscribeLocalEvent<TailedEntitySegmentComponent, MetaFlagRemoveAttemptEvent>(OnMapTrackingFlagRemoveAttempt);
        SubscribeLocalEvent<TailedEntityComponent, MapUidChangedEvent>(OnHeadMapChanged);
        SubscribeLocalEvent<TailedEntitySegmentComponent, MapUidChangedEvent>(OnSegmentMapChanged);
    }

    private void OnMapTrackingInit<T>(Entity<T> ent, ref ComponentInit args) where T : Component
    {
        if (!_netManager.IsClient)
            _metadata.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnMapTrackingRemove<T>(Entity<T> ent, ref ComponentRemove args) where T : Component
    {
        if (!_netManager.IsClient)
            _metadata.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnMapTrackingFlagRemoveAttempt<T>(Entity<T> ent, ref MetaFlagRemoveAttemptEvent args) where T : Component
    {
        if (!_netManager.IsClient && ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
    }

    private void OnHeadMapChanged(Entity<TailedEntityComponent> ent, ref MapUidChangedEvent args)
    {
        if (!_netManager.IsClient)
            ent.Comp.TailJointsDirty = true;
    }

    private void OnSegmentMapChanged(Entity<TailedEntitySegmentComponent> ent, ref MapUidChangedEvent args)
    {
        if (!_netManager.IsClient && TryComp<TailedEntityComponent>(ent.Comp.HeadEntity, out var head))
            head.TailJointsDirty = true;
    }

    private bool TryRestoreTail(Entity<TailedEntityComponent> head)
    {
        if (!CanRestoreTail(head) ||
            !TryComp<TransformComponent>(head, out var headTransform) ||
            headTransform.MapUid == null)
        {
            return false;
        }

        var previousPosition = _transform.GetWorldPosition(headTransform);
        var previousRotation = _transform.GetWorldRotation(headTransform);

        // Finish every transfer before creating joints: moving another segment may clear
        // its neighbours' joints again. NoFTL round trips have already finished at this point.
        foreach (var segment in head.Comp.TailSegments)
        {
            if (!TryComp<TransformComponent>(segment, out var segmentTransform))
                return false;

            if (segmentTransform.MapUid != headTransform.MapUid)
            {
                var position = previousPosition - previousRotation.ToWorldVec() * head.Comp.Spacing;
                _transform.SetMapCoordinates((segment, segmentTransform), new MapCoordinates(position, headTransform.MapID));

                if (!CanRestoreTailBody(head) || !CanRestoreTailBody(segment))
                    return false;

                _transform.SetWorldRotation(segment, previousRotation);
                _physics.SetLinearVelocity(segment, Vector2.Zero);
                _physics.SetAngularVelocity(segment, 0f);
            }

            previousPosition = _transform.GetWorldPosition(segmentTransform);
            previousRotation = _transform.GetWorldRotation(segmentTransform);
        }

        if (!CanRestoreTail(head) || !TryRestoreTailJoints(head))
            return false;

        head.Comp.TailJointsDirty = false;
        return true;
    }

    private bool CanRestoreTail(Entity<TailedEntityComponent> head)
    {
        if (!head.Comp.Running || !CanRestoreTailBody(head))
            return false;

        foreach (var segment in head.Comp.TailSegments)
        {
            if (!CanRestoreTailBody(segment) ||
                !TryComp<TailedEntitySegmentComponent>(segment, out var tail) ||
                !tail.Running ||
                tail.HeadEntity != head.Owner)
            {
                return false;
            }
        }

        return true;
    }

    private bool CanRestoreTailBody(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) &&
               !EntityManager.IsQueuedForDeletion(uid) &&
               TryComp<TransformComponent>(uid, out var transform) && transform.Initialized &&
               TryComp<PhysicsComponent>(uid, out var physics) && physics.Initialized;
    }

    private bool TryRestoreTailJoints(Entity<TailedEntityComponent> head)
    {
        if (!TryComp<TransformComponent>(head, out var headTransform) || headTransform.MapUid == null)
            return false;

        var previous = head.Owner;
        DisableTailJointNetworking(previous);

        foreach (var segment in head.Comp.TailSegments)
        {
            if (!CanRestoreTailBody(previous) || !CanRestoreTailBody(segment) ||
                !TryComp<TransformComponent>(segment, out var segmentTransform) ||
                segmentTransform.MapUid != headTransform.MapUid)
            {
                return false;
            }

            DisableTailJointNetworking(segment);

            var jointId = $"TailJoint_{previous}_{segment}";
            if (TryComp<JointComponent>(previous, out var joints) && joints.GetJoints.ContainsKey(jointId))
            {
                previous = segment;
                continue;
            }

            var joint = _joint.CreateDistanceJoint(
                bodyA: previous,
                bodyB: segment,
                anchorA: head.Comp.AnchorAOffset,
                anchorB: head.Comp.AnchorBOffset,
                id: jointId,
                minimumDistance: head.Comp.Spacing * 0.8f);

            joint.Length = head.Comp.Spacing;
            joint.MinLength = head.Comp.Spacing * head.Comp.MinLengthMultiplier;
            joint.MaxLength = head.Comp.Spacing * head.Comp.MaxLengthMultiplier;
            joint.Stiffness = head.Comp.Stiffness;
            joint.Damping = head.Comp.Damping;

            previous = segment;
        }

        return true;
    }
}
