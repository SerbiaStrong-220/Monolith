using Content.Server._Mono.FireControl;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Shared._Exodus.Nebula;
using Content.Shared._Mono.ShipGuns;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Applies weapon cooldown modifiers from the nebula marker containing the weapon's grid.
/// Cache-driven so gun fire checks do not resolve marker prototype components every shot.
/// </summary>
public sealed class NebulaWeaponCooldownSystem : EntitySystem
{
    private const float MinCooldownMultiplier = 0.1f;

    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private EntityQuery<AutoShootGunComponent> _autoShootGunQuery;
    private EntityQuery<FireControllableComponent> _fireControllableQuery;
    private EntityQuery<NebulaPresenceComponent> _presenceQuery;
    private EntityQuery<ShipGunClassComponent> _shipGunClassQuery;
    private EntityQuery<SpaceArtilleryComponent> _spaceArtilleryQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    private readonly Dictionary<string, WeaponCooldownMultipliers> _modifiersByMarker = new();

    public override void Initialize()
    {
        base.Initialize();

        _autoShootGunQuery = GetEntityQuery<AutoShootGunComponent>();
        _fireControllableQuery = GetEntityQuery<FireControllableComponent>();
        _presenceQuery = GetEntityQuery<NebulaPresenceComponent>();
        _shipGunClassQuery = GetEntityQuery<ShipGunClassComponent>();
        _spaceArtilleryQuery = GetEntityQuery<SpaceArtilleryComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<GunComponent, QueryFireRateMultiplierEvent>(OnQueryFireRateMultiplier);
        SubscribeLocalEvent<GunComponent, QueryGunReloadCooldownMultiplierEvent>(OnQueryReloadCooldownMultiplier);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildCache();
    }

    public TimeSpan GetModifiedReloadCooldown(EntityUid weaponUid, TimeSpan cooldown)
    {
        if (cooldown <= TimeSpan.Zero ||
            !IsShipWeapon(weaponUid) ||
            !TryGetCurrentWeaponCooldownMultipliers(weaponUid, out _, out var reloadCooldownMultiplier))
        {
            return cooldown;
        }

        return cooldown * reloadCooldownMultiplier;
    }

    public bool TryGetCurrentWeaponCooldownMultipliers(
        EntityUid weaponUid,
        out float shotCooldownMultiplier,
        out float reloadCooldownMultiplier)
    {
        shotCooldownMultiplier = 1f;
        reloadCooldownMultiplier = 1f;

        if (!_transformQuery.TryComp(weaponUid, out var xform) ||
            xform.GridUid is not { Valid: true } gridUid)
        {
            return false;
        }

        return TryGetGridWeaponCooldownMultipliers(gridUid, out shotCooldownMultiplier, out reloadCooldownMultiplier);
    }

    public bool TryGetGridWeaponCooldownMultipliers(
        EntityUid gridUid,
        out float shotCooldownMultiplier,
        out float reloadCooldownMultiplier)
    {
        shotCooldownMultiplier = 1f;
        reloadCooldownMultiplier = 1f;

        if (!_presenceQuery.TryComp(gridUid, out var presence))
            return false;

        if (presence.Marker.Id is not { } id ||
            !_modifiersByMarker.TryGetValue(id, out var multipliers))
        {
            return true;
        }

        shotCooldownMultiplier = multipliers.ShotCooldownMultiplier;
        reloadCooldownMultiplier = multipliers.ReloadCooldownMultiplier;
        return true;
    }

    private void OnQueryFireRateMultiplier(Entity<GunComponent> ent, ref QueryFireRateMultiplierEvent args)
    {
        if (!IsShipWeapon(ent.Owner) ||
            !TryGetCurrentWeaponCooldownMultipliers(ent.Owner, out var shotCooldownMultiplier, out _))
        {
            return;
        }

        args.ReloadTimeMul *= shotCooldownMultiplier;
    }

    private void OnQueryReloadCooldownMultiplier(Entity<GunComponent> ent, ref QueryGunReloadCooldownMultiplierEvent args)
    {
        if (!IsShipWeapon(ent.Owner) ||
            !TryGetCurrentWeaponCooldownMultipliers(ent.Owner, out _, out var reloadCooldownMultiplier))
        {
            return;
        }

        args.ReloadCooldownMultiplier *= reloadCooldownMultiplier;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            BuildCache();
    }

    private void BuildCache()
    {
        _modifiersByMarker.Clear();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<NebulaComponent>(out _, _componentFactory))
                continue;

            if (!proto.TryGetComponent<NebulaWeaponCooldownModifierComponent>(out var comp, _componentFactory))
                continue;

            _modifiersByMarker[proto.ID] = new WeaponCooldownMultipliers(
                SanitizeCooldownMultiplier(comp.ShotCooldownMultiplier),
                SanitizeCooldownMultiplier(comp.ReloadCooldownMultiplier));
        }
    }

    private bool IsShipWeapon(EntityUid uid)
    {
        return _autoShootGunQuery.HasComp(uid) ||
               _fireControllableQuery.HasComp(uid) ||
               _shipGunClassQuery.HasComp(uid) ||
               _spaceArtilleryQuery.HasComp(uid);
    }

    private static float SanitizeCooldownMultiplier(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            return 1f;

        return MathF.Max(MinCooldownMultiplier, multiplier);
    }

    private readonly record struct WeaponCooldownMultipliers(
        float ShotCooldownMultiplier,
        float ReloadCooldownMultiplier);
}
