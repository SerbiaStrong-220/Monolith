using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Atmos;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class VirusLifecycleSystem
{
    private readonly Dictionary<(EntityUid Host, string Strain), (VirusDescriptor Descriptor, float Chance)> _exposures = [];

    private void InitializeReservoirs()
    {
        SubscribeLocalEvent<VirusReservoirComponent, MapInitEvent>(OnReservoirInit);
    }

    private void OnReservoirInit(Entity<VirusReservoirComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Strain == null && ent.Comp.InitialVirus is { } virus)
            ent.Comp.Strain = _virology.BuildDescriptor(virus);
        if (ent.Comp.Strain is { } strain)
            ent.Comp.Identity = _virology.GetIdentity(strain);
    }

    private void ExposeReservoirs()
    {
        _exposures.Clear();
        var query = EntityQueryEnumerator<VirusReservoirComponent>();
        while (query.MoveNext(out var uid, out var reservoir))
        {
            if (reservoir.Strain is not { } strain || _containers.IsEntityInContainer(uid)
                || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid)
                || _atmos.GetContainingMixture(uid) is not { } air || air.Pressure < Atmospherics.HazardLowPressure)
                continue;

            reservoir.Identity ??= _virology.GetIdentity(strain);
            _nearby.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), reservoir.Range, _nearby);
            foreach (var (host, _) in _nearby)
            {
                if (_mobState.IsDead(host) || _containers.IsEntityInContainer(host)
                    || !_interaction.InRangeUnobstructed(uid, host, reservoir.Range))
                    continue;

                var key = (host, reservoir.Identity);
                if (!_exposures.TryGetValue(key, out var previous) || reservoir.InfectionChance > previous.Chance)
                    _exposures[key] = (strain, reservoir.InfectionChance);
            }
        }

        foreach (var (key, exposure) in _exposures)
            _virology.TryExpose(key.Host, exposure.Descriptor, VirusTransmissionVector.Proximity, exposure.Chance);
    }
}
