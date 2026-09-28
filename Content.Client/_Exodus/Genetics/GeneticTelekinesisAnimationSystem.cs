using System.Numerics;
using Content.Shared._Exodus.Genetics;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Genetics;

public sealed partial class GeneticTelekinesisAnimationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(ContainerSystem));
        UpdatesBefore.Add(typeof(SpriteTreeSystem));
        SubscribeNetworkEvent<GeneticTelekinesisAnimationEvent>(OnAnimation);
        SubscribeLocalEvent<GeneticTelekinesisAnimationComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAnimation(GeneticTelekinesisAnimationEvent args)
    {
        var uid = GetEntity(args.Item);
        if (TerminatingOrDeleted(uid) || !HasComp<SpriteComponent>(uid) || args.Duration <= TimeSpan.Zero)
            return;
        RemComp<GeneticTelekinesisAnimationComponent>(uid);
        var animation = AddComp<GeneticTelekinesisAnimationComponent>(uid);
        animation.Start = GetCoordinates(args.Start);
        animation.End = GetCoordinates(args.End);
        animation.Duration = args.Duration;
        animation.Expires = _timing.CurTime + TimeSpan.FromSeconds(1) + args.Duration;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<GeneticTelekinesisAnimationComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var animation, out var sprite, out var transform))
        {
            if (_timing.CurTime >= animation.Expires || !animation.Start.IsValid(EntityManager) || !animation.End.IsValid(EntityManager))
            {
                RemCompDeferred<GeneticTelekinesisAnimationComponent>(uid);
                continue;
            }

            // The animation message may arrive before the item's container/transform state.
            if (animation.Started == null)
            {
                if (_containers.IsEntityInContainer(uid) || !_transform.InRange(transform.Coordinates, animation.End, 0.5f))
                    continue;
                animation.OriginalOffset = sprite.Offset;
                animation.Started = _timing.CurTime;
            }

            var progress = (float) ((_timing.CurTime - animation.Started.Value).TotalSeconds / animation.Duration.TotalSeconds);
            if (progress >= 1 || _containers.IsEntityInContainer(uid))
            {
                RemCompDeferred<GeneticTelekinesisAnimationComponent>(uid);
                continue;
            }

            var start = _transform.ToMapCoordinates(animation.Start);
            var end = _transform.ToMapCoordinates(animation.End);
            if (start.MapId != end.MapId || start.MapId != transform.MapID)
            {
                RemCompDeferred<GeneticTelekinesisAnimationComponent>(uid);
                continue;
            }
            var offset = (start.Position - end.Position) * (1 - progress);
            var eyeRotation = _eye.CurrentEye.Rotation;
            var rotation = _transform.GetWorldRotation(uid);
            if (sprite.NoRotation)
                rotation = -eyeRotation;
            else if (sprite.SnapCardinals)
                rotation -= (rotation + eyeRotation).Reduced().FlipPositive().RoundToCardinalAngle();
            offset = rotation.Opposite().RotateVec(offset);
            SetSpriteOffset((uid, sprite), animation.OriginalOffset + offset);
        }
    }

    private void OnShutdown(Entity<GeneticTelekinesisAnimationComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Started != null && !TerminatingOrDeleted(ent) && TryComp<SpriteComponent>(ent, out var sprite))
            SetSpriteOffset((ent.Owner, sprite), ent.Comp.OriginalOffset);
    }

    private void SetSpriteOffset(Entity<SpriteComponent> ent, Vector2 offset)
    {
        _sprites.SetOffset(ent.AsNullable(), offset);
        // SetOffset changes the drawing matrix only. Refresh culling bounds as well, including
        // the final restoration, or a stationary item can stay indexed at a point along its flight.
        _spriteTree.QueueTreeUpdate(ent);
    }
}
