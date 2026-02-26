using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Destructible;
using Content.Shared.Construction;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Exodus.SpaceArtillery.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Audio.Systems;
using System.Numerics;

namespace Content.Shared.Exodus.SpaceArtillery;

public abstract class SharedShipGrapplingGunSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private const string ID = "ship_grappling_gun";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipGrapplingProjectileComponent, ProjectileEmbedEvent>(OnGrappleCollide);
        SubscribeLocalEvent<ShipGrapplingGunComponent, GunShotEvent>(OnGrappleShot);
        SubscribeLocalEvent<ShipGrapplingGunTargetComponent, EntityTerminatingEvent>(OnTargetDeconstructed);
        SubscribeLocalEvent<ShipGrapplingGunTargetComponent, DestructionEventArgs>(OnTargetDestruction);
    }

    private void OnGrappleShot(EntityUid uid, ShipGrapplingGunComponent component, ref GunShotEvent args)
    {
        foreach (var (shootUid, _) in args.Ammo)
        {
            if (!HasComp<ShipGrapplingProjectileComponent>(shootUid))
                continue;

            if (component.Projectile != null)
                Ungrapple((uid, component), false);

            component.Projectile = shootUid.Value;
            _gun.ChangeBasicEntityAmmoCount(uid, 1);
            PvsOverride(shootUid.Value);
            PvsOverride(uid);
            var visuals = EnsureComp<JointVisualsComponent>(shootUid.Value);
            visuals.Sprite = component.RopeSprite;
            visuals.Target = GetNetEntity(uid);
            visuals.OffsetA = new Vector2(0f, 0.5f);
            visuals.OffsetB = component.GunVisualOffset;
            Dirty(uid, component);
            Dirty(shootUid.Value, visuals);
        }
    }

    private void OnGrappleCollide(EntityUid uid, ShipGrapplingProjectileComponent component, ref ProjectileEmbedEvent args)
    {
        if (!_timing.IsFirstTimePredicted || TerminatingOrDeleted(args.Weapon))
            return;

        if (_netManager.IsClient)
            return;

        if (!TryComp<ShipGrapplingGunComponent>(args.Weapon, out var grapComp))
            return;

        var gunGridUid = Transform(args.Weapon).GridUid;
        var targetGridUid = Transform(args.Embedded).GridUid;

        if (!gunGridUid.HasValue || !targetGridUid.HasValue)
            return;

        var gunPos = _transform.GetWorldPosition(args.Weapon);
        var targetPos = _transform.GetWorldPosition(args.Embedded);

        var anchorA = Vector2.Transform(gunPos, _transform.GetInvWorldMatrix(gunGridUid.Value));
        var anchorB = Vector2.Transform(targetPos, _transform.GetInvWorldMatrix(targetGridUid.Value));

        var jointComp = EnsureComp<JointComponent>(targetGridUid.Value);
        var joint = _joints.CreateDistanceJoint(gunGridUid.Value, targetGridUid.Value, anchorA, anchorB, id: $"{ID}_{args.Weapon}");
        grapComp.JointId = joint.ID;
        joint.MaxLength = joint.Length + 10.0f;
        joint.Stiffness = 1f;

        _physics.WakeBody(gunGridUid.Value);
        _physics.WakeBody(targetGridUid.Value);

        var targetComp = EnsureComp<ShipGrapplingGunTargetComponent>(args.Embedded);
        targetComp.Gun = args.Weapon;
        var targetGridComp = EnsureComp<ShipGrapplingTargetGridComponent>(targetGridUid.Value);
        targetGridComp.Gun = args.Weapon;

        grapComp.Target = args.Embedded;
        grapComp.TargetGrid = targetGridUid.Value;
        component.Gun = args.Weapon;

        Dirty(targetGridUid.Value, jointComp);
    }

    private void OnTargetDeconstructed(EntityUid uid, ShipGrapplingGunTargetComponent component, ref EntityTerminatingEvent args)
    {
        Log.Info("OnTargetDeconstructed start");
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;
        Log.Info("OnTargetDeconstructed Ungrapple");

        Ungrapple((component.Gun, grapComp), true);
    }

    private void OnTargetDestruction(EntityUid uid, ShipGrapplingGunTargetComponent component, ref DestructionEventArgs args)
    {
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;

        Ungrapple((component.Gun, grapComp), true);
    }

    public void Ungrapple(Entity<ShipGrapplingGunComponent> gun, bool isBreak)
    {
        if (!_timing.IsFirstTimePredicted || gun.Comp.Projectile is not { } projectile)
            return;

        if (isBreak)
            _audio.PlayPvs(gun.Comp.BreakSound, gun.Owner);

        if (_netManager.IsServer)
        {
            var gunGridUid = Transform(gun.Owner).GridUid;

            if (gun.Comp.JointId != null && gunGridUid.HasValue)
                _joints.RemoveJoint(gunGridUid.Value, gun.Comp.JointId);

            if (gun.Comp.Target != null && HasComp<ShipGrapplingGunTargetComponent>(gun.Comp.Target))
                RemComp<ShipGrapplingGunTargetComponent>(gun.Comp.Target.Value);

            if (gun.Comp.TargetGrid != null && HasComp<ShipGrapplingTargetGridComponent>(gun.Comp.TargetGrid))
                RemComp<ShipGrapplingTargetGridComponent>(gun.Comp.TargetGrid.Value);

                QueueDel(gun.Comp.Projectile.Value);
                gun.Comp.Projectile = null;
        }

        gun.Comp.JointId = null;
        gun.Comp.Target = null;
        gun.Comp.TargetGrid = null;
        _gun.ChangeBasicEntityAmmoCount(gun.Owner, 1);
        RemovePvsOverride(gun.Owner);
        Dirty(gun.Owner, gun.Comp);
    }

    protected virtual void PvsOverride(EntityUid uid) { }

    protected virtual void RemovePvsOverride(EntityUid uid) { }
}
