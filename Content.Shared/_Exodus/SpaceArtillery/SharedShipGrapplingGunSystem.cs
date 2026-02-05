using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Exodus.SpaceArtillery.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Shared.Exodus.SpaceArtillery;

public abstract class SharedShipGrapplingGunSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const string ID = "ship_grappling_gun";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GrapplingProjectileComponent, ProjectileEmbedEvent>(OnGrappleCollide);
        SubscribeLocalEvent<ShipGrapplingGunComponent, GunShotEvent>(OnGrappleShot);
    }

    private void OnGrappleShot(EntityUid uid, ShipGrapplingGunComponent component, ref GunShotEvent args)
    {
        Log.Info("Grappling gun shot!");
        foreach (var (shootUid, _) in args.Ammo)
        {
            if (!HasComp<GrapplingProjectileComponent>(shootUid))
                continue;
            Log.Info("Grappling gun shot with grapple projectile!");

            component.Projectile = shootUid.Value;
            var visuals = EnsureComp<JointVisualsComponent>(shootUid.Value);
            visuals.Sprite = component.RopeSprite;
            visuals.Target = GetNetEntity(uid);
            Dirty(uid, component);
        }
    }

    private void OnGrappleCollide(EntityUid uid, GrapplingProjectileComponent component, ref ProjectileEmbedEvent args)
    {
        Log.Info("Grappling gun projectile embedded!");
        if (TerminatingOrDeleted(args.Weapon))
            return;
        Log.Info("Grappling gun projectile embedded with valid weapon!");

        if (_netManager.IsClient)
            return;
        Log.Info("Grappling gun projectile embedded on server!");

        if (!TryComp<ShipGrapplingGunComponent>(args.Weapon, out var grapComp))
            return;
        Log.Info("Grappling gun projectile embedded with valid weapon component!");

        var gunGridUid = Transform(args.Weapon).GridUid;
        var targetGridUid = Transform(args.Embedded).GridUid;

        if (!gunGridUid.HasValue || !targetGridUid.HasValue)
            return;
        Log.Info("Grappling gun projectile embedded with valid grid UIDs!");

        var gunPos = _transform.GetWorldPosition(args.Weapon);
        var targetPos = _transform.GetWorldPosition(args.Embedded);

        var anchorA = Vector2.Transform(gunPos, _transform.GetInvWorldMatrix(gunGridUid.Value));
        var anchorB = Vector2.Transform(targetPos, _transform.GetInvWorldMatrix(targetGridUid.Value));

        var jointComp = EnsureComp<JointComponent>(targetGridUid.Value);
        var joint = _joints.CreateDistanceJoint(gunGridUid.Value, targetGridUid.Value, anchorA, anchorB);

        joint.ID = $"{ID}_{args.Weapon.ToString()}";
        joint.MaxLength = joint.Length + 25.0f;
        joint.Stiffness = 1f;

        Dirty(args.Embedded, jointComp);
    }
}
