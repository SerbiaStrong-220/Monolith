// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Content.Server.Shuttles.Events;
using Content.Server._Exodus.SpaceArtillery.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._Exodus.SpaceArtillery;
using Content.Shared._Exodus.SpaceArtillery.Components;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Physics.Systems;
using System.Numerics;

namespace Content.Server._Exodus.SpaceArtillery;

public sealed class ShipGrapplingGunSystem : SharedShipGrapplingGunSystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PvsOverrideSystem _override = default!;

    private EntityQuery<ShipGrapplingGunComponent> _grapQuerry;

    private const string JointID = "ship_grappling_gun";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipGrapplingProjectileComponent, ProjectileEmbedEvent>(OnGrappleCollide);
        SubscribeLocalEvent<ShipGrapplingTargetGridComponent, FTLStartedEvent>(OnFTLStart);

        SubscribeLocalEvent<ShipGrapplingGunTargetComponent, EntityTerminatingEvent>(OnTargetTerminating);
        SubscribeLocalEvent<ShipGrapplingProjectileComponent, EntityTerminatingEvent>(OnProjectileTerminating);
        SubscribeLocalEvent<ShipGrapplingGunComponent, EntityTerminatingEvent>(OnGunTerminating);

        SubscribeLocalEvent<ShipGrapplingGunComponent, GetVerbsEvent<ActivationVerb>>(OnGetUngrappleVerbs);

        _grapQuerry = GetEntityQuery<ShipGrapplingGunComponent>();
    }

    public override void Update(float frameTime)
    {
        var projQuerry = EntityQueryEnumerator<ShipGrapplingProjectileComponent, TransformComponent>();

        while (projQuerry.MoveNext(out var uid, out var projComp, out var xform))
        {
            var gunUid = projComp.Gun;

            if (!_grapQuerry.TryGetComponent(gunUid, out var grapComp))
                continue;

            var currentCoords = xform.Coordinates;

            if (!currentCoords.TryDistance(EntityManager, _transform, projComp.LocalGunShotPos, out var distance))
                continue;

            if (distance >= grapComp.MaxLength)
                Ungrapple((gunUid, grapComp), false);
        }
    }

    private void OnGetUngrappleVerbs(Entity<ShipGrapplingGunComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var gunGrid = Transform(ent).GridUid;
        var targetGrid = ent.Comp.TargetGrid;

        if (ent.Comp.Projectile == null || !gunGrid.HasValue || !targetGrid.HasValue || gunGrid == targetGrid)
            return;

        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("ship-grappling-gun-verb-ungrapple"),
            Act = () => Ungrapple(ent, false),
        });
    }

    private void OnGrappleCollide(EntityUid uid, ShipGrapplingProjectileComponent component, ref ProjectileEmbedEvent args)
    {
        if (TerminatingOrDeleted(args.Weapon))
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

        var joint = _joints.CreateDistanceJoint(gunGridUid.Value, targetGridUid.Value, anchorA, anchorB, id: $"{JointID}_{args.Weapon}");

        joint.MinLength = 0;
        joint.MaxLength = joint.Length + grapComp.JointOffset;
        joint.Stiffness = grapComp.Stiffness;

        grapComp.JointId = joint.ID;
        grapComp.Target = args.Embedded;
        grapComp.TargetGrid = targetGridUid.Value;

        _physics.WakeBody(gunGridUid.Value);
        _physics.WakeBody(targetGridUid.Value);

        var targetComp = EnsureComp<ShipGrapplingGunTargetComponent>(args.Embedded);
        targetComp.Gun = args.Weapon;

        var targetGridComp = EnsureComp<ShipGrapplingTargetGridComponent>(targetGridUid.Value);
        targetGridComp.Gun = args.Weapon;

        var ev = new ShipGrappleEvent();
        RaiseLocalEvent(ev);

        Dirty(args.Embedded, targetComp);
    }

    private void OnFTLStart(EntityUid uid, ShipGrapplingTargetGridComponent component, ref FTLStartedEvent args)
    {
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;

        Ungrapple((component.Gun, grapComp), true);
    }

    private void OnTargetTerminating(EntityUid uid, ShipGrapplingGunTargetComponent component, ref EntityTerminatingEvent args)
    {
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;

        if (grapComp.Target != uid)
            return;

        Ungrapple((component.Gun, grapComp), true);
    }

    private void OnProjectileTerminating(EntityUid uid, ShipGrapplingProjectileComponent component, ref EntityTerminatingEvent args)
    {
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;

        if (grapComp.Projectile != uid)
            return;

        Ungrapple((component.Gun, grapComp), true);
    }

    private void OnGunTerminating(EntityUid uid, ShipGrapplingGunComponent component, ref EntityTerminatingEvent args)
    {
        Ungrapple((uid, component), true);
    }

    protected override void Ungrapple(Entity<ShipGrapplingGunComponent> gun, bool isBreak)
    {
        if (gun.Comp.Projectile is not { } projectile)
            return;

        var gunGridUid = Transform(gun.Owner).GridUid;

        if (isBreak)
            _audio.PlayPvs(gun.Comp.BreakSound, gun.Owner);

        _appearance.SetData(gun.Owner, SharedTetherGunSystem.TetherVisualsStatus.Key, true);

        RemovePvsOverride(gun.Owner);

        if (gun.Comp.JointId != null && gunGridUid.HasValue)
            _joints.RemoveJoint(gunGridUid.Value, gun.Comp.JointId);

        if (gun.Comp.Target != null && HasComp<ShipGrapplingGunTargetComponent>(gun.Comp.Target))
            RemComp<ShipGrapplingGunTargetComponent>(gun.Comp.Target.Value);

        if (gun.Comp.TargetGrid != null && HasComp<ShipGrapplingTargetGridComponent>(gun.Comp.TargetGrid))
            RemComp<ShipGrapplingTargetGridComponent>(gun.Comp.TargetGrid.Value);

        QueueDel(gun.Comp.Projectile.Value);

        gun.Comp.Projectile = null;
        gun.Comp.JointId = null;
        gun.Comp.Target = null;
        gun.Comp.TargetGrid = null;

        _gun.ChangeBasicEntityAmmoCount(gun.Owner, 1);

        var ev = new ShipUngrappleEvent();
        RaiseLocalEvent(ev);

        Dirty(gun.Owner, gun.Comp);
    }

    protected override void PvsOverride(EntityUid uid)
    {
        base.PvsOverride(uid);

        _override.AddGlobalOverride(uid);
    }

    protected override void RemovePvsOverride(EntityUid uid)
    {
        base.RemovePvsOverride(uid);

        _override.RemoveGlobalOverride(uid);
    }
}

public sealed class ShipGrappleEvent : EntityEventArgs {}

public sealed class ShipUngrappleEvent : EntityEventArgs {}
