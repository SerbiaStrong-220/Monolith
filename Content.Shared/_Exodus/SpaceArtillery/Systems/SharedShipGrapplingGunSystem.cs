// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Content.Shared.Physics;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._Exodus.SpaceArtillery.Components;
using System.Numerics;

namespace Content.Shared._Exodus.SpaceArtillery;

public abstract class SharedShipGrapplingGunSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipGrapplingGunComponent, GunShotEvent>(OnGrappleShot);

    }

    private void OnGrappleShot(EntityUid uid, ShipGrapplingGunComponent component, ref GunShotEvent args)
    {
        foreach (var (shootUid, _) in args.Ammo)
        {
            if (!TryComp<ShipGrapplingProjectileComponent>(shootUid, out var projComp))
                continue;

            if (component.Projectile != null)
                Ungrapple((uid, component), false);

            component.Projectile = shootUid.Value;

            PvsOverride(uid);
            PvsOverride(shootUid.Value);

            var visuals = EnsureComp<JointVisualsComponent>(shootUid.Value);
            visuals.Sprite = component.RopeSprite;
            visuals.Target = GetNetEntity(uid);
            visuals.OffsetA = new Vector2(0f, 0.5f);
            visuals.OffsetB = component.GunVisualOffset;

            projComp.Gun = uid;
            projComp.LocalGunShotPos = Transform(uid).Coordinates;

            _gun.ChangeBasicEntityAmmoCount(uid, 1);

            Dirty(uid, component);
            Dirty(shootUid.Value, visuals);
            Dirty(shootUid.Value, projComp);
        }

        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, SharedTetherGunSystem.TetherVisualsStatus.Key, false, appearance);
    }

    protected virtual void Ungrapple(Entity<ShipGrapplingGunComponent> gun, bool isBreak) {}

    protected virtual void PvsOverride(EntityUid uid) { }

    protected virtual void RemovePvsOverride(EntityUid uid) { }
}
