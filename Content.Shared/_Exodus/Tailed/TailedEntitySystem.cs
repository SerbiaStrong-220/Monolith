// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Tailed;

/// <summary>
/// This system connects all segments of tailed entity.
/// Simply spawn segments with some offsets and initializes joints for them.
/// The worst part is tailed mob movement which is placed in SharedMoverController.
///
/// Probably this system can be used for any other tailed entities other than mob,
/// but I had enough with all this shit, adapt it for your conditions on your own.
/// </summary>
public sealed partial class TailedEntitySystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedJointSystem _joint = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TailedEntityComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TailedEntityComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<TailedEntitySegmentComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<TailedEntitySegmentComponent, ComponentShutdown>(OnSegmentShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TailedEntityComponent>();
        while (query.MoveNext(out var uid, out var tailed))
        {
            UpdateTailedMob((uid, tailed), frameTime);
        }
    }

    private void OnDamageChanged(EntityUid uid, TailedEntitySegmentComponent component, DamageChangedEvent args)
    {
        if (!TryComp<DamageableComponent>(component.HeadEntity, out var headDamageable))
            return;

        if (args.DamageDelta is not { } damage)
            _damageable.SetDamage(component.HeadEntity, headDamageable, args.Damageable.Damage);
        else
            _damageable.TryChangeDamage(component.HeadEntity, damage, true, true, headDamageable, args.Origin);
    }

    private void OnComponentStartup(EntityUid uid, TailedEntityComponent component, ComponentStartup args)
    {
        if (component.TailSegments.Count == 0)
            InitializeTailSegments((uid, component, Transform(uid)));
    }

    private void OnComponentShutdown(EntityUid uid, TailedEntityComponent component, ComponentShutdown args)
    {
        foreach (var segment in component.TailSegments)
        {
            if (!TerminatingOrDeleted(segment) && !EntityManager.IsQueuedForDeletion(segment))
            {
                _joint.ClearJoints(segment);
                QueueDel(segment);
            }
        }
        component.TailSegments.Clear();
    }

    private void OnSegmentShutdown(EntityUid uid, TailedEntitySegmentComponent component, ComponentShutdown args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        _joint.ClearJoints(uid);
        QueueDel(component.HeadEntity);
    }

    private void InitializeTailSegments(Entity<TailedEntityComponent, TransformComponent> ent)
    {
        var (uid, comp, xform) = ent;

        var mapUid = xform.MapUid;
        if (mapUid == null)
            return;

        // Ensure the head entity has physics for joints
        if (!HasComp<PhysicsComponent>(uid))
            return;

        var headPos = _transform.GetWorldPosition(xform);
        var headRot = _transform.GetWorldRotation(xform);

        comp.TailSegments.Clear();

        for (var i = 0; i < comp.Amount; i++)
        {
            var offset = headRot.ToWorldVec() * comp.Spacing * (i + 1);
            var spawnPos = headPos - offset;

            var segment = PredictedSpawnAtPosition(comp.Prototype, new EntityCoordinates(mapUid.Value, spawnPos));

            _transform.SetWorldRotation(segment, headRot);

            var tail = EnsureComp<TailedEntitySegmentComponent>(segment);
            tail.HeadEntity = uid;
            tail.Index = i;
            comp.TailSegments.Add(segment);
        }

        var prev = uid;

        foreach (var segment in comp.TailSegments)
        {
            // Ensure segment has physics before creating joint
            if (!HasComp<PhysicsComponent>(segment))
                continue;

            var joint = _joint.CreateDistanceJoint(
                bodyA: prev,
                bodyB: segment,
                anchorA: comp.AnchorAOffset,
                anchorB: comp.AnchorBOffset,
                minimumDistance: comp.Spacing * 0.8f
            );

            joint.Length = comp.Spacing;
            joint.MinLength = comp.Spacing * comp.MinLengthMultiplier;
            joint.MaxLength = comp.Spacing * comp.MaxLengthMultiplier;

            joint.Stiffness = comp.Stiffness;
            joint.Damping = comp.Damping;

            joint.ID = $"TailJoint_{prev}_{segment}";

            prev = segment;
        }
    }

    private void UpdateTailedMob(Entity<TailedEntityComponent> head, float frameTime)
    {
        if (head.Comp.TailSegments.Count == 0)
            return;

        foreach (var segment in head.Comp.TailSegments)
        {
            if (TerminatingOrDeleted(segment))
                return;
        }

        CalculateSegmentTargets(head, out var targetPositions);

        ApplySegmentVelocities(head.Comp, targetPositions, frameTime);

        UpdateSegmentRotation(head, frameTime);
    }

    private void CalculateSegmentTargets(
        Entity<TailedEntityComponent> head,
        out Vector2[] targetPositions)
    {
        targetPositions = new Vector2[head.Comp.TailSegments.Count];

        var headPos = _transform.GetWorldPosition(head);
        var headDir = _transform.GetWorldRotation(head).ToWorldVec();

        targetPositions[0] = headPos - headDir * head.Comp.Spacing;

        for (var i = 1; i < head.Comp.TailSegments.Count; i++)
        {
            var prevSegment = head.Comp.TailSegments[i - 1];
            var prevPos = _transform.GetWorldPosition(prevSegment);
            var prevDir = _transform.GetWorldRotation(prevSegment).ToWorldVec();

            targetPositions[i] = prevPos - prevDir * head.Comp.Spacing;
        }
    }

    private void ApplySegmentVelocities(
        TailedEntityComponent tail,
        Vector2[] targetPositions,
        float frameTime)
    {
        var prevPos = Vector2.Zero;
        EntityUid? prevEntity = null;

        for (var i = 0; i < tail.TailSegments.Count; i++)
        {
            var segment = tail.TailSegments[i];

            if (!TryComp<PhysicsComponent>(segment, out var physics))
                continue;

            var currentPos = _transform.GetWorldPosition(segment);
            Vector2 desiredVelocity;

            if (prevEntity != null)
            {
                var toPrev = prevPos - currentPos;
                var currentDistance = toPrev.Length();
                var directionToPrev = toPrev.Normalized();

                if (currentDistance < tail.Spacing * tail.MinLengthMultiplier)
                {
                    desiredVelocity = -directionToPrev * tail.MaxSegmentSpeed * 0.5f;
                }
                else if (currentDistance > tail.Spacing * tail.MaxLengthMultiplier)
                {
                    desiredVelocity = directionToPrev * tail.MaxSegmentSpeed;
                }
                else
                {
                    var targetPos = targetPositions[i];
                    var toTarget = targetPos - currentPos;
                    desiredVelocity = toTarget * tail.FollowSharpness;
                }
            }
            else
            {
                var targetPos = targetPositions[i];
                var toTarget = targetPos - currentPos;
                desiredVelocity = toTarget * tail.FollowSharpness;
            }

            if (desiredVelocity.Length() > tail.MaxSegmentSpeed)
            {
                desiredVelocity = desiredVelocity.Normalized() * tail.MaxSegmentSpeed;
            }

            var currentVelocity = physics.LinearVelocity;

            var newVelocity = Vector2.Lerp(
                currentVelocity,
                desiredVelocity,
                frameTime * tail.VelocitySmoothing);

            _physics.SetLinearVelocity(segment, newVelocity, body: physics);

            prevEntity = segment;
            prevPos = currentPos;
        }
    }

    private void UpdateSegmentRotation(
        Entity<TailedEntityComponent> head,
        float frameTime)
    {
        if (!head.Comp.EnableRotationControl) return;

        var prevPos = _transform.GetWorldPosition(head);

        for (var i = 0; i < head.Comp.TailSegments.Count; i++)
        {
            var segment = head.Comp.TailSegments[i];

            var segmentPos = _transform.GetWorldPosition(segment);

            var direction = prevPos - segmentPos;

            if (direction.LengthSquared() > 0.1f)
            {
                var targetAngle = NormalizeAngle(MathF.Atan2(direction.Y, direction.X) + head.Comp.RotationModifier);

                var currentAngle = _transform.GetWorldRotation(segment);

                var newAngle = Angle.Lerp(
                    currentAngle,
                    targetAngle,
                    frameTime * head.Comp.RotationLerpSpeed);

                _transform.SetWorldRotation(segment, newAngle);
            }

            prevPos = segmentPos;
        }
    }

    private static Angle NormalizeAngle(Angle angle)
    {
        angle %= MathHelper.TwoPi;
        if (angle < 0)
            angle += MathHelper.TwoPi;
        return angle;
    }
}
