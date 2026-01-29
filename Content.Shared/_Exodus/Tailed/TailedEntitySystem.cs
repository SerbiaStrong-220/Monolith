// (c) Space Exodus Team - MPL-2.0 with CLA
// Authors: Lokilife
using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Exodus.Tailed;

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
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedJointSystem _joint = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TailedEntityComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TailedEntityComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<TailedEntitySegmentComponent, DamageChangedEvent>(OnDamageChanged);
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
            QueueDel(segment);

        component.TailSegments.Clear();
    }

    private void InitializeTailSegments(Entity<TailedEntityComponent, TransformComponent> ent)
    {
        var (uid, comp, xform) = ent;

        var mapUid = xform.MapUid;
        if (mapUid == null)
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
}
