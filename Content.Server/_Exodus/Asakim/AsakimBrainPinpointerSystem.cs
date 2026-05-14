using Content.Server.Pinpointer;
using Content.Shared._Exodus.Asakim;
using Content.Shared.Body.Components;
using Content.Shared.Interaction;
using Content.Shared.Pinpointer;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._Exodus.Asakim;

public sealed class AsakimBrainPinpointerSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> AsakimBrainTag = "AsakimBrain";

    [Dependency] private readonly AsakimIdentitySystem _asakim = default!;
    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<AsakimBrainPinpointerComponent, ActivateInWorldEvent>(OnActivate, after: [typeof(PinpointerSystem)]);
    }

    private void OnActivate(Entity<AsakimBrainPinpointerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || !TryComp<PinpointerComponent>(ent, out var pinpointer) || !pinpointer.IsActive)
            return;

        _pinpointer.SetTarget(ent, FindNearestAsakim(ent, Transform(ent)), pinpointer);
    }

    private EntityUid? FindNearestAsakim(EntityUid source, TransformComponent sourceTransform)
    {
        var mapId = sourceTransform.MapID;
        var sourcePosition = _transform.GetWorldPosition(sourceTransform);
        var nearestDistance = float.MaxValue;
        EntityUid? nearest = null;

        var bodyQuery = EntityQueryEnumerator<BodyComponent, TransformComponent>();
        while (bodyQuery.MoveNext(out var bodyUid, out _, out var bodyTransform))
        {
            if (bodyUid == source || bodyTransform.MapID != mapId || !_asakim.HasAsakimBrain(bodyUid))
                continue;

            TrySetNearest(bodyUid, bodyTransform, sourcePosition, ref nearestDistance, ref nearest);
        }

        var tagQuery = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (tagQuery.MoveNext(out var brainUid, out var tag, out var brainTransform))
        {
            if (brainUid == source || brainTransform.MapID != mapId || !_tag.HasTag(tag, AsakimBrainTag))
                continue;

            TrySetNearest(brainUid, brainTransform, sourcePosition, ref nearestDistance, ref nearest);
        }

        return nearest;
    }

    private void TrySetNearest(
        EntityUid target,
        TransformComponent targetTransform,
        Vector2 sourcePosition,
        ref float nearestDistance,
        ref EntityUid? nearest)
    {
        var distance = (_transform.GetWorldPosition(targetTransform, _transformQuery) - sourcePosition).LengthSquared();
        if (distance >= nearestDistance)
            return;

        nearestDistance = distance;
        nearest = target;
    }
}
