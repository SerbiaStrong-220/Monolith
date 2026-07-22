// (c) Space Exodus Team
using Content.Shared._Exodus.ShipArmor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using System.Numerics;

namespace Content.Server._Exodus.ShipArmor;

/// <summary>
/// Registers local ship armor on grids, absorbs in-radius damage, regenerates via sparse active set.
/// </summary>
public sealed class ShipArmorSystem : SharedShipArmorSystem
{
    private const float ArmorBucketSize = 4f;
    private const float ProtectionBoundsPadding = 0.001f;

    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<ShipArmorComponent> _armorQuery;
    private EntityQuery<ShipArmorGridComponent> _gridArmorQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _armorQuery = GetEntityQuery<ShipArmorComponent>();
        _gridArmorQuery = GetEntityQuery<ShipArmorGridComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ShipArmorComponent, ComponentStartup>(OnArmorStartup);
        SubscribeLocalEvent<ShipArmorComponent, ComponentShutdown>(OnArmorShutdown);
        SubscribeLocalEvent<ShipArmorComponent, AnchorStateChangedEvent>(OnArmorAnchorChanged);
        SubscribeLocalEvent<ShipArmorComponent, EntParentChangedMessage>(OnArmorParentChanged);
        SubscribeLocalEvent<ShipArmorComponent, MoveEvent>(OnArmorMoved);

        // Damageable is ubiquitous — early-outs must stay cheap (grid HasComp).
        SubscribeLocalEvent<DamageableComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<ShipArmorGridComponent, ShipArmorTileDamageEvent>(OnTileDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = Timing.CurTime;
        var query = EntityQueryEnumerator<ActiveShipArmorComponent, ShipArmorComponent>();

        while (query.MoveNext(out var uid, out _, out var armor))
        {
            if (!_xformQuery.TryGetComponent(uid, out var xform) || !xform.Anchored || xform.GridUid is null)
            {
                RemCompDeferred<ActiveShipArmorComponent>(uid);
                continue;
            }

            if (armor.CurrentCharge >= armor.MaxCharge)
            {
                armor.CurrentCharge = armor.MaxCharge;
                RemCompDeferred<ActiveShipArmorComponent>(uid);
                Dirty(uid, armor);
                continue;
            }

            if (armor.NextUpdate > curTime)
                continue;

            var interval = armor.RegenInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : armor.RegenInterval;

            armor.NextUpdate += interval;
            if (armor.NextUpdate < curTime)
                armor.NextUpdate = curTime + interval;

            var regen = armor.RegenRate * (float)interval.TotalSeconds;
            if (regen <= FixedPoint2.Zero)
                continue;

            var old = armor.CurrentCharge;
            armor.CurrentCharge = FixedPoint2.Min(armor.MaxCharge, armor.CurrentCharge + regen);
            Dirty(uid, armor);

            var changed = new ShipArmorChargeChangedEvent(old, armor.CurrentCharge, armor.MaxCharge);
            RaiseLocalEvent(uid, ref changed);

            if (armor.CurrentCharge >= armor.MaxCharge)
                RemCompDeferred<ActiveShipArmorComponent>(uid);
        }
    }

    private void OnArmorStartup(Entity<ShipArmorComponent> ent, ref ComponentStartup args)
    {
        TryRegister(ent);
    }

    private void OnArmorShutdown(Entity<ShipArmorComponent> ent, ref ComponentShutdown args)
    {
        if (_xformQuery.TryGetComponent(ent.Owner, out var xform))
            RemoveFromIndex(xform.GridUid, ent.Owner);

        RemCompDeferred<ActiveShipArmorComponent>(ent.Owner);
    }

    private void OnArmorAnchorChanged(Entity<ShipArmorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            TryRegister(ent);
        else
        {
            if (_xformQuery.TryGetComponent(ent.Owner, out var xform))
                RemoveFromIndex(xform.GridUid, ent.Owner);

            RemCompDeferred<ActiveShipArmorComponent>(ent.Owner);
        }
    }

    private void OnArmorParentChanged(Entity<ShipArmorComponent> ent, ref EntParentChangedMessage args)
    {
        RemoveFromIndex(ResolveGridUid(args.OldParent), ent.Owner);
        if (!TryRegister(ent))
            RemCompDeferred<ActiveShipArmorComponent>(ent.Owner);
    }

    private void OnArmorMoved(Entity<ShipArmorComponent> ent, ref MoveEvent args)
    {
        var xform = args.Component;
        if (!xform.Anchored || xform.GridUid is not { } grid)
            return;

        if (!_gridArmorQuery.TryGetComponent(grid, out var gridArmor))
            return;

        if (gridArmor.Armors.ContainsKey(ent.Owner))
        {
            UpdateArmorIndex(gridArmor, ent.Owner, xform.LocalPosition);
            RebuildProtectionBounds(gridArmor);
        }
    }

    private void OnDamageModify(EntityUid uid, DamageableComponent _, DamageModifyEvent args)
    {
        TryApplyArmor(uid, args.Damage, args.ArmorPenetration);
    }

    private void OnBeforeDamageChanged(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.OriginFlag != DamageableSystem.DamageOriginFlag.Explosion)
            return;

        // Explosions skip DamageModifyEvent. Intercept them here so splash damage
        // is consumed by nearby armor charge before it reaches the fragile target.
        TryApplyArmor(ent.Owner, args.Damage, 0f);
    }

    private void OnTileDamage(Entity<ShipArmorGridComponent> ent, ref ShipArmorTileDamageEvent args)
    {
        if (args.Cancelled || args.Grid != ent.Owner || args.Damage <= FixedPoint2.Zero)
            return;

        if (ent.Comp.Buckets.Count == 0)
            return;

        if (!_mapGridQuery.TryGetComponent(ent.Owner, out var grid))
            return;

        var targetLocal = _map.GridTileToLocal(ent.Owner, grid, args.Tile).Position;
        if (!IsWithinProtectionBounds(ent.Comp, targetLocal))
            return;

        var absorbed = TryApplyTileArmor(ent.Comp, targetLocal, args.Damage);
        args.Cancelled = absorbed >= args.Damage;
    }

    private void TryApplyArmor(EntityUid uid, DamageSpecifier damage, float armorPenetration)
    {
        if (!damage.AnyPositive())
            return;

        if (!_xformQuery.TryGetComponent(uid, out var xform) ||
            !xform.Anchored ||
            xform.GridUid is not { } grid)
            return;

        // Fast path: most grids never have ship armor.
        if (!_gridArmorQuery.TryGetComponent(grid, out var gridArmor) || gridArmor.Buckets.Count == 0)
            return;

        var targetLocal = xform.LocalPosition;
        if (xform.ParentUid != grid)
        {
            if (!_xformQuery.TryGetComponent(grid, out var gridXform))
                return;

            targetLocal = Vector2.Transform(
                _transform.GetWorldPosition(xform),
                _transform.GetInvWorldMatrix(gridXform));
        }

        if (!IsWithinProtectionBounds(gridArmor, targetLocal))
            return;

        var searchRadius = Math.Max(0f, gridArmor.MaxRadius);
        var minBucket = GetArmorBucket(targetLocal - new Vector2(searchRadius));
        var maxBucket = GetArmorBucket(targetLocal + new Vector2(searchRadius));

        for (var bucketX = minBucket.X; bucketX <= maxBucket.X; bucketX++)
        {
            for (var bucketY = minBucket.Y; bucketY <= maxBucket.Y; bucketY++)
            {
                if (!gridArmor.Buckets.TryGetValue(new Vector2i(bucketX, bucketY), out var bucket))
                    continue;

                foreach (var armorUid in bucket)
                {
                    if (!damage.AnyPositive())
                        return;

                    if (!gridArmor.Armors.TryGetValue(armorUid, out var armorEntry))
                        continue;

                    if (!_armorQuery.TryGetComponent(armorUid, out var armor) || !armor.Enabled)
                        continue;

                    if (armor.CurrentCharge <= FixedPoint2.Zero)
                        continue;

                    // Self is always in range.
                    if (armorUid != uid)
                    {
                        var delta = targetLocal - armorEntry.LocalPosition;
                        var radius = NormalizeRadius(armor.Radius);
                        if (delta.LengthSquared() > radius * radius)
                            continue;
                    }

                    TryAbsorb((armorUid, armor), damage, armorPenetration);
                }
            }
        }
    }

    private FixedPoint2 TryApplyTileArmor(
        ShipArmorGridComponent gridArmor,
        Vector2 targetLocal,
        FixedPoint2 damage)
    {
        var remaining = damage;
        var absorbedTotal = FixedPoint2.Zero;
        var searchRadius = Math.Max(0f, gridArmor.MaxRadius);
        var minBucket = GetArmorBucket(targetLocal - new Vector2(searchRadius));
        var maxBucket = GetArmorBucket(targetLocal + new Vector2(searchRadius));

        for (var bucketX = minBucket.X; bucketX <= maxBucket.X; bucketX++)
        {
            for (var bucketY = minBucket.Y; bucketY <= maxBucket.Y; bucketY++)
            {
                if (!gridArmor.Buckets.TryGetValue(new Vector2i(bucketX, bucketY), out var bucket))
                    continue;

                foreach (var armorUid in bucket)
                {
                    if (remaining <= FixedPoint2.Zero)
                        return absorbedTotal;

                    if (!gridArmor.Armors.TryGetValue(armorUid, out var armorEntry))
                        continue;

                    if (!_armorQuery.TryGetComponent(armorUid, out var armor) || !armor.Enabled)
                        continue;

                    if (armor.CurrentCharge <= FixedPoint2.Zero)
                        continue;

                    var delta = targetLocal - armorEntry.LocalPosition;
                    var radius = NormalizeRadius(armor.Radius);
                    if (delta.LengthSquared() > radius * radius)
                        continue;

                    var absorbed = TryAbsorbAmount((armorUid, armor), remaining, "Structural", 0f);
                    remaining -= absorbed;
                    absorbedTotal += absorbed;
                }
            }
        }

        return absorbedTotal;
    }

    private bool TryRegister(Entity<ShipArmorComponent> ent)
    {
        if (!_xformQuery.TryGetComponent(ent.Owner, out var xform))
            return false;

        if (!xform.Anchored || xform.GridUid is not { } grid)
            return false;

        if (!_mapGridQuery.HasComp(grid))
            return false;

        var gridArmor = EnsureComp<ShipArmorGridComponent>(grid);
        UpdateArmorIndex(gridArmor, ent.Owner, xform.LocalPosition);
        RebuildProtectionBounds(gridArmor);

        if (ent.Comp.CurrentCharge < ent.Comp.MaxCharge)
            EnsureComp<ActiveShipArmorComponent>(ent.Owner);

        return true;
    }

    private void RemoveFromIndex(EntityUid? grid, EntityUid armor)
    {
        if (grid is not { } gridUid)
            return;

        if (!_gridArmorQuery.TryGetComponent(gridUid, out var gridArmor))
            return;

        if (!gridArmor.Armors.Remove(armor, out var armorEntry))
            return;

        RemoveFromBucket(gridArmor, armor, armorEntry.Bucket);

        if (gridArmor.Armors.Count == 0)
        {
            gridArmor.HasProtectionBounds = false;
            RemCompDeferred<ShipArmorGridComponent>(gridUid);
        }
        else
        {
            RebuildProtectionBounds(gridArmor);
        }
    }

    private static Vector2i GetArmorBucket(Vector2 localPosition)
    {
        return new(
            (int) MathF.Floor(localPosition.X / ArmorBucketSize),
            (int) MathF.Floor(localPosition.Y / ArmorBucketSize));
    }

    private static float NormalizeRadius(float radius)
    {
        return float.IsFinite(radius) ? Math.Max(0f, radius) : 0f;
    }

    private static void UpdateArmorIndex(
        ShipArmorGridComponent gridArmor,
        EntityUid armor,
        Vector2 localPosition)
    {
        var bucket = GetArmorBucket(localPosition);

        if (gridArmor.Armors.TryGetValue(armor, out var previous) && previous.Bucket != bucket)
            RemoveFromBucket(gridArmor, armor, previous.Bucket);

        gridArmor.Armors[armor] = new ShipArmorIndexEntry(localPosition, bucket);

        if (!gridArmor.Buckets.TryGetValue(bucket, out var bucketEntities))
        {
            bucketEntities = new HashSet<EntityUid>();
            gridArmor.Buckets.Add(bucket, bucketEntities);
        }

        bucketEntities.Add(armor);
    }

    private void RebuildProtectionBounds(ShipArmorGridComponent gridArmor)
    {
        if (gridArmor.Armors.Count == 0)
        {
            gridArmor.HasProtectionBounds = false;
            gridArmor.MaxRadius = 0f;
            return;
        }

        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        var maxRadius = 0f;
        var foundArmor = false;

        foreach (var (armorUid, armorEntry) in gridArmor.Armors)
        {
            if (!_armorQuery.TryGetComponent(armorUid, out var armor))
                continue;

            var radius = NormalizeRadius(armor.Radius);
            var radiusVector = new Vector2(radius);
            min = Vector2.Min(min, armorEntry.LocalPosition - radiusVector);
            max = Vector2.Max(max, armorEntry.LocalPosition + radiusVector);
            maxRadius = Math.Max(maxRadius, radius);
            foundArmor = true;
        }

        gridArmor.HasProtectionBounds = foundArmor;
        gridArmor.ProtectionBoundsMin = min;
        gridArmor.ProtectionBoundsMax = max;
        gridArmor.MaxRadius = maxRadius;
    }

    private static bool IsWithinProtectionBounds(ShipArmorGridComponent gridArmor, Vector2 targetLocal)
    {
        if (!gridArmor.HasProtectionBounds)
            return false;

        return targetLocal.X >= gridArmor.ProtectionBoundsMin.X - ProtectionBoundsPadding
            && targetLocal.Y >= gridArmor.ProtectionBoundsMin.Y - ProtectionBoundsPadding
            && targetLocal.X <= gridArmor.ProtectionBoundsMax.X + ProtectionBoundsPadding
            && targetLocal.Y <= gridArmor.ProtectionBoundsMax.Y + ProtectionBoundsPadding;
    }

    private static void RemoveFromBucket(ShipArmorGridComponent gridArmor, EntityUid armor, Vector2i bucket)
    {
        if (!gridArmor.Buckets.TryGetValue(bucket, out var bucketEntities))
            return;

        bucketEntities.Remove(armor);
        if (bucketEntities.Count == 0)
            gridArmor.Buckets.Remove(bucket);
    }

    private EntityUid? ResolveGridUid(EntityUid? entity)
    {
        if (entity is not { } uid)
            return null;

        if (_mapGridQuery.HasComp(uid))
            return uid;

        if (_xformQuery.TryGetComponent(uid, out var xform))
            return xform.GridUid;

        return null;
    }
}
