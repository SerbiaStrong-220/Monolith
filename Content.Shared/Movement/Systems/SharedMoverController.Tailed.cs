// (c) Space Exodus Team - MPL-2.0 with CLA
// Authors: Lokilife
using System.Numerics;
using Content.Shared.Exodus.Tailed;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    private void UpdateTailedMob(EntityUid headUid, float frameTime)
    {
        if (!TryComp<TailedEntityComponent>(headUid, out var tail))
            return;

        if (tail.TailSegments.Count == 0)
            return;

        CalculateSegmentTargets(headUid, tail, out var targetPositions);

        ApplySegmentVelocities(tail, targetPositions, frameTime);

        UpdateSegmentRotation(headUid, tail, frameTime);
    }

    private void CalculateSegmentTargets(
        EntityUid head,
        TailedEntityComponent tail,
        out Vector2[] targetPositions)
    {
        targetPositions = new Vector2[tail.TailSegments.Count];

        var headPos = _transform.GetWorldPosition(head);
        var headDir = _transform.GetWorldRotation(head).ToWorldVec();

        targetPositions[0] = headPos - headDir * tail.Spacing;

        for (var i = 1; i < tail.TailSegments.Count; i++)
        {
            var prevSegment = tail.TailSegments[i - 1];
            var prevPos = _transform.GetWorldPosition(prevSegment);
            var prevDir = _transform.GetWorldRotation(prevSegment).ToWorldVec();

            targetPositions[i] = prevPos - prevDir * tail.Spacing;
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

            if (i == 0)
            {
                if (!TryComp<TailedEntityComponent>(segment, out var tailComp))
                    continue;
            }

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

            PhysicsSystem.SetLinearVelocity(segment, newVelocity, body: physics);

            prevEntity = segment;
            prevPos = currentPos;
        }
    }

    private void UpdateSegmentRotation(
        EntityUid headUid,
        TailedEntityComponent tail,
        float frameTime)
    {
        if (!tail.EnableRotationControl) return;

        var prevPos = _transform.GetWorldPosition(headUid);

        for (var i = 0; i < tail.TailSegments.Count; i++)
        {
            var segment = tail.TailSegments[i];
            var segmentPos = _transform.GetWorldPosition(segment);

            var direction = prevPos - segmentPos;

            if (direction.LengthSquared() > 0.1f)
            {
                var targetAngle = NormalizeAngle(MathF.Atan2(direction.Y, direction.X) + tail.RotationModifier);

                var currentAngle = _transform.GetWorldRotation(segment);

                var newAngle = Angle.Lerp(
                    currentAngle,
                    targetAngle,
                    frameTime * tail.RotationLerpSpeed);

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
