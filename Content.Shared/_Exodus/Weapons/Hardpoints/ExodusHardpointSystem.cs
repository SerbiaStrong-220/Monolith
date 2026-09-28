using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

public sealed partial class ExodusHardpointSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    private EntityQuery<ExodusHardpointComponent> _hardpointQuery;
    private EntityQuery<TransformComponent> _transformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();
        _hardpointQuery = GetEntityQuery<ExodusHardpointComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ExodusHardpointComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ExodusHardpointWeaponComponent, ExaminedEvent>(OnWeaponExamined);
        SubscribeLocalEvent<ExodusHardpointWeaponComponent, QueryFireRateMultiplierEvent>(OnQueryFireRate);
        SubscribeLocalEvent<ExodusHardpointWeaponComponent, QueryGunReloadCooldownMultiplierEvent>(OnQueryBurstCooldown);
    }

    /// <summary>
    /// Returns the best compatible mount's firing rate fraction, or the configured minimum without a mount.
    /// </summary>
    public float GetFireRateMultiplier(Entity<ExodusHardpointWeaponComponent> ent)
    {
        var minimumRate = ent.Comp.MinimumFireRateMultiplier;
        if (!(minimumRate > 0f && minimumRate <= 1f))
            minimumRate = 0.5f;

        if (!_transformQuery.TryGetComponent(ent, out var xform) || !xform.Anchored ||
            xform.GridUid is not { } gridUid || xform.ParentUid != gridUid ||
            !_gridQuery.TryGetComponent(gridUid, out var grid))
            return minimumRate;

        var oversizePenalty = ent.Comp.OversizeFireRatePenalty;
        if (!(oversizePenalty >= 0f && oversizePenalty <= 1f))
            oversizePenalty = 0.25f;

        // Query only this gun's tile when it fires. Re-checking live components prevents
        // stale compatibility after unanchoring, deletion, grid splitting or snapshot restoration.
        var tile = _map.LocalToTile(gridUid, grid, xform.Coordinates);
        var enumerator = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        var multiplier = minimumRate;
        while (enumerator.MoveNext(out var uid))
        {
            if (!_hardpointQuery.TryGetComponent(uid, out var hardpoint) ||
                hardpoint.LifeStage >= ComponentLifeStage.Stopping ||
                !_transformQuery.TryGetComponent(uid, out var mountTransform) ||
                !mountTransform.Anchored || mountTransform.ParentUid != gridUid)
                continue;

            if (hardpoint.Class != ExodusHardpointClass.Universal &&
                ent.Comp.Class != ExodusHardpointClass.Universal && hardpoint.Class != ent.Comp.Class)
                continue;

            var sizeDifference = Math.Max(0, (int) ent.Comp.Size - (int) hardpoint.Size);
            var rate = 1f - sizeDifference * oversizePenalty;
            if (rate >= 1f)
                return 1f;

            // Overlapping mounts provide only the best rate, never a cumulative modifier.
            multiplier = MathF.Max(multiplier, rate);
        }

        return multiplier;
    }

    private void OnQueryFireRate(Entity<ExodusHardpointWeaponComponent> ent, ref QueryFireRateMultiplierEvent args)
    {
        args.ReloadTimeMul /= GetFireRateMultiplier(ent);
    }

    private void OnQueryBurstCooldown(Entity<ExodusHardpointWeaponComponent> ent, ref QueryGunReloadCooldownMultiplierEvent args)
    {
        args.ReloadCooldownMultiplier /= GetFireRateMultiplier(ent);
    }

    private void OnExamined(Entity<ExodusHardpointComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("exodus-hardpoint-examine",
            ("class", Loc.GetString("exodus-hardpoint-class", ("class", ent.Comp.Class.ToString()))),
            ("size", Loc.GetString("exodus-hardpoint-size", ("size", ent.Comp.Size.ToString())))));
    }

    private void OnWeaponExamined(Entity<ExodusHardpointWeaponComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("exodus-hardpoint-weapon-examine",
            ("class", Loc.GetString("exodus-hardpoint-class", ("class", ent.Comp.Class.ToString()))),
            ("size", Loc.GetString("exodus-hardpoint-size", ("size", ent.Comp.Size.ToString()))),
            ("percent", GetFireRateMultiplier(ent) * 100f)));
    }
}
