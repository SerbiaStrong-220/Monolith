using Content.Server._Exodus.Body;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Atmos;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHabitatSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private RespiratorSystem _respirator = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    private EntityQuery<RotHabitatComponent> _habitats;

    public override void Initialize()
    {
        base.Initialize();
        _habitats = GetEntityQuery<RotHabitatComponent>();
        SubscribeLocalEvent<RotCreatureComponent, RespirationAttemptEvent>(OnRespiration);
        SubscribeLocalEvent<RotCreatureComponent, EnvironmentDamageAttemptEvent>(OnEnvironmentDamage);
    }

    public bool IsSheltered(EntityUid uid)
    {
        var xform = Transform(uid);
        if (_containers.IsEntityInContainer(uid) || xform.GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        var tile = _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
        foreach (var surface in _maps.GetAnchoredEntities(grid, mapGrid, tile))
        {
            if (_habitats.HasComp(surface) && !TerminatingOrDeleted(surface)
                && !EntityManager.IsQueuedForDeletion(surface) && !_mobs.IsDead(surface))
                return true;
        }
        return false;
    }

    private void OnRespiration(Entity<RotCreatureComponent> ent, ref RespirationAttemptEvent args)
    {
        if (!IsSheltered(ent) || !TryComp<RespiratorComponent>(ent, out var respirator))
            return;
        args.Handled = true;
        _respirator.UpdateSaturation(ent, respirator.MaxSaturation, respirator);
    }

    private void OnEnvironmentDamage(Entity<RotCreatureComponent> ent, ref EnvironmentDamageAttemptEvent args)
    {
        if (args.Cancelled || !IsSheltered(ent))
            return;
        // The tissue protects against vacuum cooling, not cold rooms or cold weapons.
        if (args.Hazard == EnvironmentHazard.Cold
            && _atmosphere.GetContainingMixture(ent.Owner) is { } air && air.Pressure >= Atmospherics.HazardLowPressure)
            return;
        args.Cancelled = true;
    }
}
