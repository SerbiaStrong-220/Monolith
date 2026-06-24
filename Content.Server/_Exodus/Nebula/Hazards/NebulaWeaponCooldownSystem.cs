using Content.Server._Mono.FireControl;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared._Mono.ShipGuns;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Hazards;

/// <summary>
/// Applies weapon cooldown modifiers from the nebula marker containing the weapon's grid,
/// plus per-weapon nebula rate multipliers and resistance.
/// Cache-driven so gun fire checks do not resolve marker prototype components every shot.
/// </summary>
public sealed partial class NebulaWeaponCooldownSystem : EntitySystem
{
    private const float MinCooldownMultiplier = 0.1f;

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;

    private EntityQuery<AutoShootGunComponent> _autoShootGunQuery;
    private EntityQuery<FireControllableComponent> _fireControllableQuery;
    private EntityQuery<NebulaPresenceComponent> _presenceQuery;
    private EntityQuery<NebulaWeaponCooldownMultiplierComponent> _weaponMultiplierQuery;
    private EntityQuery<NebulaWeaponCooldownResistanceComponent> _weaponResistanceQuery;
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
        _weaponMultiplierQuery = GetEntityQuery<NebulaWeaponCooldownMultiplierComponent>();
        _weaponResistanceQuery = GetEntityQuery<NebulaWeaponCooldownResistanceComponent>();
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

        if (!TryGetGridWeaponCooldownMultipliers(gridUid, out var nebulaShotMultiplier, out var nebulaReloadMultiplier))
            return false;

        var shotRateMultiplier = GetWeaponShotCooldownMultiplier(weaponUid);
        var reloadRateMultiplier = GetWeaponReloadCooldownMultiplier(weaponUid);
        var shotResistance = GetShotCooldownResistance(weaponUid);
        var reloadResistance = GetReloadCooldownResistance(weaponUid);

        shotCooldownMultiplier = GetEffectiveCooldownMultiplier(
            nebulaShotMultiplier,
            shotResistance,
            shotRateMultiplier);
        reloadCooldownMultiplier = GetEffectiveCooldownMultiplier(
            nebulaReloadMultiplier,
            reloadResistance,
            reloadRateMultiplier);

        return true;
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

    public float GetWeaponShotCooldownMultiplier(EntityUid weaponUid)
    {
        if (!_weaponMultiplierQuery.TryComp(weaponUid, out var multiplier))
            return 1f;

        return SanitizeRateMultiplier(multiplier.ShotCooldownMultiplier);
    }

    public float GetWeaponReloadCooldownMultiplier(EntityUid weaponUid)
    {
        if (!_weaponMultiplierQuery.TryComp(weaponUid, out var multiplier))
            return 1f;

        return SanitizeRateMultiplier(multiplier.ReloadCooldownMultiplier);
    }

    public float GetShotCooldownResistance(EntityUid weaponUid)
    {
        if (!_weaponResistanceQuery.TryComp(weaponUid, out var resistance))
            return 0f;

        return SanitizeResistance(resistance.ShotCooldownResistance);
    }

    public float GetReloadCooldownResistance(EntityUid weaponUid)
    {
        if (!_weaponResistanceQuery.TryComp(weaponUid, out var resistance))
            return 0f;

        return SanitizeResistance(resistance.ReloadCooldownResistance);
    }

    private static float GetEffectiveCooldownMultiplier(
        float nebulaCooldownMultiplier,
        float resistance,
        float weaponRateMultiplier)
    {
        // Weapon multiplier is rate-style: 1.25 means cooldown / 1.25, not cooldown * 1.25.
        return GetResistedNebulaCooldownMultiplier(nebulaCooldownMultiplier, resistance) /
            weaponRateMultiplier /
            GetOverdriveMultiplier(nebulaCooldownMultiplier, resistance);
    }

    private static float GetResistedNebulaCooldownMultiplier(float nebulaCooldownMultiplier, float resistance)
    {
        // 0..1 is pure resistance to the marker cooldown modifier:
        // 0 gets the full modifier, 0.5 gets half of its distance from neutral, 1 ignores it.
        var effectiveResistance = MathF.Min(resistance, 1f);
        return MathF.Max(MinCooldownMultiplier, 1f + (nebulaCooldownMultiplier - 1f) * (1f - effectiveResistance));
    }

    private static float GetOverdriveMultiplier(float nebulaCooldownMultiplier, float resistance)
    {
        if (nebulaCooldownMultiplier == 1f)
            return 1f;

        // Values above 1 become a direct rate multiplier after the marker modifier is ignored.
        // Example: purple x4 and resistance 1.6 gives marker x1, then cooldown / 1.6.
        return MathF.Max(1f, resistance);
    }

    private static float SanitizeCooldownMultiplier(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            return 1f;

        return MathF.Max(MinCooldownMultiplier, multiplier);
    }

    private static float SanitizeRateMultiplier(float multiplier)
    {
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
            return 1f;

        return MathF.Max(MinCooldownMultiplier, multiplier);
    }

    private static float SanitizeResistance(float resistance)
    {
        if (float.IsNaN(resistance) || float.IsInfinity(resistance))
            return 1f;

        return MathF.Max(0f, resistance);
    }

    private readonly record struct WeaponCooldownMultipliers(
        float ShotCooldownMultiplier,
        float ReloadCooldownMultiplier);
}
