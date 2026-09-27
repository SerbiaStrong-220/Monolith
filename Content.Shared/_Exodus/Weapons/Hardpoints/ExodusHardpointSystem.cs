using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map.Components;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

public sealed class ExodusHardpointSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

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
    }

    /// <summary>
    /// Returns the strongest hardpoint bonus on this gun's current tile, or 1 if none applies.
    /// </summary>
    public float GetFireIntervalMultiplier(Entity<GunComponent> ent)
    {
        if (!_transformQuery.TryGetComponent(ent, out var xform) || !xform.Anchored ||
            xform.GridUid is not { } gridUid || xform.ParentUid != gridUid ||
            !_gridQuery.TryGetComponent(gridUid, out var grid))
            return 1f;

        // Query only this gun's tile when it fires. Re-checking live components prevents
        // stale bonuses after unanchoring, deletion, grid splitting or snapshot restoration.
        var tile = _map.LocalToTile(gridUid, grid, xform.Coordinates);
        var enumerator = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        var multiplier = 1f;
        while (enumerator.MoveNext(out var uid))
        {
            if (!_hardpointQuery.TryGetComponent(uid, out var hardpoint) ||
                hardpoint.LifeStage >= ComponentLifeStage.Stopping ||
                !_transformQuery.TryGetComponent(uid, out var mountTransform) ||
                !mountTransform.Anchored || mountTransform.ParentUid != gridUid)
                continue;

            // Overlapping mounts grant only the strongest valid bonus, never a product.
            if (hardpoint.FireIntervalMultiplier > 0f && hardpoint.FireIntervalMultiplier < multiplier)
                multiplier = hardpoint.FireIntervalMultiplier;
        }

        return multiplier;
    }

    private void OnExamined(Entity<ExodusHardpointComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !(ent.Comp.FireIntervalMultiplier > 0f && ent.Comp.FireIntervalMultiplier < 1f))
            return;

        args.PushMarkup(Loc.GetString("exodus-hardpoint-examine",
            ("percent", (1f / ent.Comp.FireIntervalMultiplier - 1f) * 100f)));
    }
}
