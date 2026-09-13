using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Atmos;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class VirusLifecycleSystem
{
    private readonly Dictionary<(EntityUid Host, string Strain), (VirusDescriptor Descriptor, float Chance)> _exposures = [];
    private readonly HashSet<(EntityUid Host, string Strain)> _ineligibleReservoirHosts = [];

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
        _ineligibleReservoirHosts.Clear();
        var query = EntityQueryEnumerator<VirusReservoirComponent>();
        while (query.MoveNext(out var uid, out var reservoir))
        {
            if (reservoir.Strain is not { } strain || reservoir.InfectionChance <= 0f
                || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid)
                || _containers.IsEntityInContainer(uid))
                continue;

            _nearby.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), reservoir.Range, _nearby);
            if (_nearby.Count == 0)
                continue;

            reservoir.Identity ??= _virology.GetIdentity(strain);
            var atmosphereChecked = false;
            foreach (var (host, _) in _nearby)
            {
                if (_mobState.IsDead(host) || _containers.IsEntityInContainer(host))
                    continue;

                var key = (host, reservoir.Identity);
                // Check overlapping sources only if they could increase this host's exposure.
                if (_ineligibleReservoirHosts.Contains(key)
                    || _exposures.TryGetValue(key, out var previous) && previous.Chance >= reservoir.InfectionChance)
                    continue;

                if (!_virology.CanAcquireVirus(host, strain))
                {
                    _ineligibleReservoirHosts.Add(key);
                    continue;
                }

                // Unoccupied or already infected areas need no atmosphere or obstruction checks.
                if (!atmosphereChecked)
                {
                    if (_atmos.GetContainingMixture(uid) is not { } air || air.Pressure < Atmospherics.HazardLowPressure)
                        break;

                    atmosphereChecked = true;
                }

                if (_interaction.InRangeUnobstructed(uid, host, reservoir.Range))
                    _exposures[key] = (strain, reservoir.InfectionChance);
            }
        }

        foreach (var (key, exposure) in _exposures)
            _virology.TryExpose(key.Host, exposure.Descriptor, VirusTransmissionVector.Proximity, exposure.Chance);
    }
}
