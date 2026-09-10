using Content.Shared._Exodus.Weapons.DistanceFalloff;
using Content.Shared._Mono.Projectile;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._Exodus.Weapons.GravityPulse;

/// <summary>Shares hit history across a volley. Motion and shooter exclusions use the normal projectile system.</summary>
public sealed class GravityPulseSystem : EntitySystem
{
    private EntityQuery<GravityPulseComponent> _pulseQuery;

    public override void Initialize()
    {
        base.Initialize();
        _pulseQuery = GetEntityQuery<GravityPulseComponent>();
        SubscribeLocalEvent<GravityPulseComponent, ProjectileHitEvent>(OnHit,
            before: new[] { typeof(DistanceFalloffSystem), typeof(ProjectileKnockbackSystem) });
        SubscribeLocalEvent<GravityPulseLauncherComponent, AmmoShotEvent>(OnVolley);
    }

    private void OnVolley(Entity<GravityPulseLauncherComponent> ent, ref AmmoShotEvent args)
    {
        if (!ent.Comp.ShareHits)
            return;

        HashSet<EntityUid>? hits = null;
        foreach (var uid in args.FiredProjectiles)
        {
            if (!_pulseQuery.TryComp(uid, out var pulse))
                continue;

            pulse.HitEntities = hits ??= new HashSet<EntityUid>();
        }
    }

    private void OnHit(Entity<GravityPulseComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Handled || (ent.Comp.HitEntities ??= new HashSet<EntityUid>()).Add(args.Target))
            return;

        // The other pellet already hit this target. Consume this pellet too, so it cannot cross a wall.
        args.Handled = true;
        QueueDel(ent);
    }
}
