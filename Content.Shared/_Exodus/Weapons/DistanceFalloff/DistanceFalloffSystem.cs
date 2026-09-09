using Content.Shared._Exodus.Weapons.Projectiles;
using Content.Shared._Mono.Projectile;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Weapons.DistanceFalloff;

public sealed class DistanceFalloffSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DistanceFalloffComponent, ProjectileShotEvent>(OnShot);
        SubscribeLocalEvent<DistanceFalloffComponent, ProjectileHitEvent>(OnHit,
            before: new[] { typeof(ProjectileKnockbackSystem) });
        SubscribeLocalEvent<DistanceFalloffComponent, ProjectileKnockbackEvent>(OnKnockback);
    }

    private void OnShot(Entity<DistanceFalloffComponent> ent, ref ProjectileShotEvent args)
    {
        ent.Comp.Origin = _transform.GetMoverCoordinates(ent);
        Dirty(ent);
    }

    private void OnHit(Entity<DistanceFalloffComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Handled)
            return;

        if (TerminatingOrDeleted(args.Target) ||
            !TryGetStrength(ent, Transform(args.Target).Coordinates, out var strength) || strength <= 0f)
        {
            args.Handled = true;
            if (_net.IsServer)
                QueueDel(ent);
            return;
        }

        // Do not mutate the prototype damage or the specifier seen by another hit.
        args.Damage *= strength;
    }

    private void OnKnockback(Entity<DistanceFalloffComponent> ent, ref ProjectileKnockbackEvent args)
    {
        if (TerminatingOrDeleted(args.Target) ||
            !TryGetStrength(ent, Transform(args.Target).Coordinates, out var strength))
        {
            args.Impulse = 0f;
            return;
        }

        args.Impulse *= strength;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<DistanceFalloffComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var falloff, out var xform))
        {
            if (falloff.Origin == null || TerminatingOrDeleted(uid))
                continue;

            if (!TryGetStrength((uid, falloff), xform.Coordinates, out var strength) || strength <= 0f)
                QueueDel(uid);
        }
    }

    public float GetStrength(Entity<DistanceFalloffComponent> ent, float distance)
    {
        var comp = ent.Comp;
        if (!float.IsFinite(distance) || !float.IsFinite(comp.Range) || comp.Range <= 0f ||
            !float.IsFinite(comp.FullStrengthDistance) || !float.IsFinite(comp.Exponent) || comp.Exponent <= 0f ||
            distance >= comp.Range)
            return 0f;

        var fullStrengthDistance = Math.Clamp(comp.FullStrengthDistance, 0f, comp.Range);
        if (distance <= fullStrengthDistance)
            return 1f;

        return MathF.Pow((comp.Range - distance) / (comp.Range - fullStrengthDistance), comp.Exponent);
    }

    public bool TryGetStrength(Entity<DistanceFalloffComponent> ent, EntityCoordinates coordinates, out float strength)
    {
        strength = 0f;
        if (ent.Comp.Origin is not { } origin || !origin.IsValid(EntityManager) ||
            TerminatingOrDeleted(origin.EntityId) || !coordinates.IsValid(EntityManager) ||
            !origin.TryDistance(EntityManager, _transform, coordinates, out var distance))
            return false;

        strength = GetStrength(ent, distance);
        return true;
    }
}
