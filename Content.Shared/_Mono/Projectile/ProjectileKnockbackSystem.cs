using Content.Shared.Projectiles;
using Content.Shared._Exodus.Weapons.Projectiles; // Exodus projectile impulse modifiers
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using System.Numerics;

namespace Content.Shared._Mono.Projectile;

public sealed partial class ProjectileKnockbackSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<PhysicsComponent> _physQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileKnockbackComponent, ProjectileHitEvent>(OnHit);

        _physQuery = GetEntityQuery<PhysicsComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
    }

    private void OnHit(Entity<ProjectileKnockbackComponent> ent, ref ProjectileHitEvent args)
    {
        // Exodus: cancelled or deduplicated hits must not impart an impulse.
        if (args.Handled)
            return;

        if (!_physQuery.TryComp(args.Target, out var targetBody)
            || !_physQuery.TryComp(ent, out var selfBody)
        )
            return;

        var toEnt = args.Target;
        // Exodus: skip both static structures and direct grid hits when configured.
        if (!ent.Comp.AffectGrids && ((targetBody.BodyType & BodyType.Static) != 0 || _gridQuery.HasComp(toEnt)))
            return;

        if ((targetBody.BodyType & BodyType.Static) != 0)
            toEnt = Transform(toEnt).ParentUid;

        if (toEnt != args.Target && !_physQuery.TryComp(toEnt, out targetBody))
            return;

        var selfCoord = new EntityCoordinates(ent, Vector2.Zero); // Exodus: no separate transform lookup needed.
        var impulseCoord = _transform.WithEntityId(selfCoord, toEnt);

        // velocity is in world rotation frame so no need to translate it
        var dirVec = selfBody.LinearVelocity;
        // Exodus: a stationary projectile has no valid knockback direction.
        if (!float.IsFinite(dirVec.X) || !float.IsFinite(dirVec.Y) || dirVec.LengthSquared() <= 0f)
            return;
        dirVec.Normalize();

        var pos = impulseCoord.Position;
        // scale distance of application point to body center to scale rotation impulse
        pos = (pos - targetBody.LocalCenter) * ent.Comp.RotateMultiplier + targetBody.LocalCenter;
        // Exodus-begin: let shared projectile effects scale the impulse (e.g. distance falloff).
        var impulse = new ProjectileKnockbackEvent(args.Target, ent.Comp.Knockback);
        RaiseLocalEvent(ent, ref impulse);
        if (float.IsFinite(impulse.Impulse) && impulse.Impulse > 0f)
            _physics.ApplyLinearImpulse(toEnt, dirVec * impulse.Impulse, pos);
        // Exodus-end
    }
}
