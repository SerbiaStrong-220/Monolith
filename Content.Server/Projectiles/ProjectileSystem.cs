using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics; // Mono;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server.Projectiles;

public sealed partial class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private DestructibleSystem _destructibleSystem = default!;

    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    // <Mono>
    private EntityQuery<PhysicsComponent> _physQuery;
    private EntityQuery<FixturesComponent> _fixQuery;


    public override void Initialize()
    {
        base.Initialize();

        // Mono
        _physQuery = GetEntityQuery<PhysicsComponent>();
        _fixQuery = GetEntityQuery<FixturesComponent>();
        // Mono
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override DamageSpecifier? ProjectileCollide(Entity<ProjectileComponent, PhysicsComponent> projectile, EntityUid target, MapCoordinates? collisionCoordinates, bool predicted = false)
    {
        var (uid, component, ourBody) = projectile;
        // Check if projectile is already spent (server-specific check)
        if (component.ProjectileSpent)
            return null;

        var otherName = ToPrettyString(target);
        // Get damage required for destructible before base applies damage
        var damageRequired = FixedPoint2.Zero;
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
        {
            damageRequired = _destructibleSystem.DestroyedAt(target);
            damageRequired -= damageableComponent.TotalDamage;
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        // var deleted = Deleted(target); // Mono: Unused

        // Call base implementation to handle damage application and other effects
        var modifiedDamage = base.ProjectileCollide(projectile, target, collisionCoordinates, predicted);

        if (modifiedDamage == null)
        {
            // mono start
            if (!component.NoDamageDelete)
                return null;

            var spEv = new ProjectileSpentEvent();
            RaiseLocalEvent(uid, spEv);
            // mono end

            component.ProjectileSpent = true;
            if (component.DeleteOnCollide)
                QueueDel(uid);
            return null;
        }

        // Server-specific logic: penetration
        if (component.PenetrationThreshold != 0)
        {
            // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
            if (component.PenetrationDamageTypeRequirement != null)
            {
                var stopPenetration = false;
                foreach (var requiredDamageType in component.PenetrationDamageTypeRequirement)
                {
                    if (!modifiedDamage.DamageDict.Keys.Contains(requiredDamageType))
                    {
                        stopPenetration = true;
                        break;
                    }
                }

                if (stopPenetration)
                    component.ProjectileSpent = true;
            }

            // If the object won't be destroyed, it "tanks" the penetration hit.
            if (modifiedDamage.GetTotal() < damageRequired)
            {
                component.ProjectileSpent = true;
            }

            if (!component.ProjectileSpent)
            {
                component.PenetrationAmount += damageRequired;
                // The projectile has dealt enough damage to be spent.
                if (component.PenetrationAmount >= component.PenetrationThreshold)
                {
                    component.ProjectileSpent = true;
                }
            }
        }
        else
        {
            component.ProjectileSpent = true;
        }

        // Mono
        if (component.ProjectileSpent)
        {
            var spEv = new ProjectileSpentEvent();
            RaiseLocalEvent(uid, spEv);
            if (component.DeleteOnCollide)
                QueueDel(uid);
        }

        return modifiedDamage;
    }

    // Exodus-OptimizeProjectiles-Start
    private readonly List<(EntityUid Uid, ProjectileComponent Comp, PhysicsComponent Body, TransformComponent Xform, Fixture ProjFix)>
                  _projectiles = [];
    private readonly List<ProjectileRayData> _rayData = [];

    private struct ProjectileRayData
    {
        public EntityUid Uid;
        public MapId MapId;
        public CollisionRay Ray;
        public float MaxDistance;
        public EntityUid Ignore;
        public IEnumerable<RayCastResults> Hits;
        public Vector2 Velocity;
    }
    // Exodus-OptimizeProjectiles-End

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Exodus-OptimizeProjectiles-Start: Gather all projectiles and raycast data in parallel, then process hits sequentially
        _projectiles.Clear();
        _rayData.Clear();

        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent>();

        while (query.MoveNext(out var uid, out var comp, out var body))
        {
            if (comp.ProjectileSpent || TerminatingOrDeleted(uid))
                continue;

            // Exodus-Start
            // we don't raycast inert projectiles
            if (comp is { Weapon: null, OnlyCollideWhenShot: true })
                continue;
            // Exodus-End

            var xform = Transform(uid);
            var vel = comp.RaycastResetVelocity ?? _physics.GetMapLinearVelocity(uid, body, xform);
            var velLen = vel.Length();
            if (!ShouldRaycastProjectile(velLen) && comp.RaycastResetVelocity == null)
                continue;

            var rayDistance = velLen * frameTime;
            if (rayDistance <= 0f)
                continue;

            if (!_fixQuery.TryComp(uid, out var fix) || !fix.Fixtures.TryGetValue(ProjectileFixture, out var projFix))
                continue;

            _projectiles.Add((uid, comp, body, xform, projFix));
        }

        _rayData.EnsureCapacity(_projectiles.Count);

        for (var i = 0; i < _projectiles.Count; i++)
        {
            var (uid, comp, body, xform, projFix) = _projectiles[i];
            var vel = comp.RaycastResetVelocity ?? _physics.GetMapLinearVelocity(uid, body, xform);
            var velLen = vel.Length();
            var rayDir = vel / velLen;
            var lastMap = _transformSystem.GetMapCoordinates(xform);
            var rayDist = velLen * frameTime;

            _rayData.Add(new ProjectileRayData
            {
                Uid = uid,
                MapId = xform.MapID,
                Ray = new CollisionRay(lastMap.Position, rayDir, projFix.CollisionMask),
                MaxDistance = rayDist,
                Ignore = uid,
                Hits = [],
                Velocity = vel,
            });
        }

        Parallel.For(0, _rayData.Count, i =>
        {
            var data = _rayData[i];
            var hits = _physics.IntersectRay(data.MapId, data.Ray, data.MaxDistance, data.Ignore, false);
            // Sort by distance so the sequential phase can just pick the first valid one
            var hitsList = (List<RayCastResults>)hits;
            hitsList.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            data.Hits = hitsList;
            _rayData[i] = data;
        });

        for (var i = 0; i < _projectiles.Count; i++)
        {
            var (uid, comp, body, xform, projFix) = _projectiles[i];
            var data = _rayData[i];

            if (!ProcessHitsSequential(uid, comp, body, xform, projFix, data.Hits, data.Velocity, frameTime))
            {
                // no valid hit – reset velocity if needed
                if (comp.RaycastResetVelocity is { } resetVel)
                {
                    var parentVel = _physics.GetMapLinearVelocity(xform.ParentUid);
                    var resetTo = resetVel - parentVel;
                    _physics.SetLinearVelocity(uid, resetTo, body: body);
                    comp.RaycastResetVelocity = null;
                }
            }

            bool ProcessHitsSequential(EntityUid uid, ProjectileComponent comp, PhysicsComponent body,
                TransformComponent xform, Fixture projFix, IEnumerable<RayCastResults> sortedHits, Vector2 velocity, float frameTime)
            {
                foreach (var hit in sortedHits)
                {
                    var hitEnt = hit.HitEntity;

                    if (!_physQuery.TryComp(hitEnt, out var otherBody) || !_fixQuery.TryComp(hitEnt, out var otherFix))
                        continue;

                    Fixture? hitFix = null;
                    foreach (var kv in otherFix.Fixtures)
                    {
                        if (kv.Value.Hard)
                        {
                            hitFix = kv.Value;
                            break;
                        }
                    }
                    if (hitFix == null) continue;

                    var ourEv = new PreventCollideEvent(uid, hitEnt, body, otherBody, projFix, hitFix);
                    RaiseLocalEvent(uid, ref ourEv);
                    if (ourEv.Cancelled) continue;

                    var otherEv = new PreventCollideEvent(hitEnt, uid, otherBody, body, hitFix, projFix);
                    RaiseLocalEvent(hitEnt, ref otherEv);
                    if (otherEv.Cancelled) continue;

                    // Valid hit – apply it
                    var lastMap = _transformSystem.GetMapCoordinates(xform);
                    var hitMapCoord = lastMap.Offset(hit.Distance * data.Ray.Direction);
                    var hitPos = _transformSystem.ToCoordinates(hitMapCoord);
                    var hitXform = Transform(hitEnt);
                    if (hitXform.Coordinates.EntityId != hitXform.GridUid && hitXform.GridUid != null)
                        hitPos = _transformSystem.WithEntityId(hitPos, hitXform.GridUid.Value);

                    // Set velocity for the frame of impact
                    if (comp.RaycastResetVelocity == null)
                    {
                        var parentVel = _physics.GetMapLinearVelocity(xform.ParentUid);
                        comp.RaycastResetVelocity = velocity + parentVel;
                        var curVel = body.LinearVelocity;
                        curVel.Normalize();
                        curVel *= 1f / frameTime;
                        _physics.SetLinearVelocity(uid, curVel, body: body);
                    }

                    _transformSystem.SetCoordinates(uid, hitPos);
                    return true;
                }
                return false;
            }
        }
        // Exodus-OptimizeProjectiles-End
    }
}
