using Content.Server._Exodus.Power.Components;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Power.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Power;

namespace Content.Server._Exodus.Power.EntitySystems;

public sealed partial class PressurePowerGeneratorSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PressurePowerGeneratorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PressurePowerGeneratorComponent, AtmosDeviceEnabledEvent>(OnAtmosDeviceEnabled);
        SubscribeLocalEvent<PressurePowerGeneratorComponent, AtmosDeviceDisabledEvent>(OnAtmosDeviceDisabled);
        SubscribeLocalEvent<PressurePowerGeneratorComponent, AtmosDeviceUpdateEvent>(OnAtmosDeviceUpdate);
    }

    private void OnMapInit(Entity<PressurePowerGeneratorComponent> ent, ref MapInitEvent args)
    {
        UpdatePressureState(ent);
    }

    private void OnAtmosDeviceEnabled(Entity<PressurePowerGeneratorComponent> ent, ref AtmosDeviceEnabledEvent args)
    {
        UpdatePressureState(ent);
    }

    private void OnAtmosDeviceDisabled(Entity<PressurePowerGeneratorComponent> ent, ref AtmosDeviceDisabledEvent args)
    {
        SetRunning(ent, false);
    }

    private void OnAtmosDeviceUpdate(Entity<PressurePowerGeneratorComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        UpdatePressureState(ent, args.Grid, args.Map);
    }

    private void UpdatePressureState(
        Entity<PressurePowerGeneratorComponent> ent,
        Entity<GridAtmosphereComponent?, GasTileOverlayComponent?>? grid = null,
        Entity<MapAtmosphereComponent?>? map = null)
    {
        var mixture = grid == null && map == null
            ? _atmosphere.GetContainingMixture(ent.Owner)
            : _atmosphere.GetContainingMixture(ent.Owner, grid, map);

        var running = mixture is not null && mixture.Pressure >= ent.Comp.MinimumPressure;
        SetRunning(ent, running);
    }

    private void SetRunning(Entity<PressurePowerGeneratorComponent> ent, bool running)
    {
        TryComp<PowerSupplierComponent>(ent.Owner, out var supplier);

        if (ent.Comp.Running == running && (supplier == null || supplier.Enabled == running))
            return;

        ent.Comp.Running = running;

        if (supplier != null)
            supplier.Enabled = running;

        _appearance.SetData(ent.Owner, PowerDeviceVisuals.Powered, running);
    }
}
