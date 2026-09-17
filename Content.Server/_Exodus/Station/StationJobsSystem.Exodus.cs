using Content.Server.Station.Components;
using Content.Shared._Exodus.Station;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Station.Systems;

public sealed partial class StationJobsSystem
{
    /// <summary>
    /// Changes staffing through a records console. Unlike taking or returning a vacancy, this changes
    /// total capacity and must respect the station's per-job limits, including occupied positions.
    /// </summary>
    public bool TryAdjustJobCapacity(Entity<StationJobsComponent?> station, ProtoId<JobPrototype> job, int amount)
    {
        if (!Resolve(station.Owner, ref station.Comp, false) || amount is not (-1 or 1) ||
            !station.Comp.JobList.TryGetValue(job, out var available) || available is not { } vacancies)
        {
            return false;
        }

        var occupied = 0;
        foreach (var jobs in station.Comp.PlayerJobs.Values)
        {
            foreach (var assignedJob in jobs)
            {
                if (assignedJob == job)
                    occupied++;
            }
        }

        station.Comp.JobLimits.TryGetValue(job, out var limits);
        var state = GetJobCapacityState(vacancies, occupied, limits);
        if (amount < 0 ? !state.CanDecrease : !state.CanIncrease)
            return false;

        return TryAdjustJobSlot(station.Owner, job, amount, stationJobs: station.Comp);
    }

    /// <summary>Builds a console snapshot without recounting assignments for each job.</summary>
    public Dictionary<ProtoId<JobPrototype>, JobCapacityState>? GetJobCapacity(Entity<StationJobsComponent?> station)
    {
        if (!Resolve(station.Owner, ref station.Comp, false))
            return null;

        var occupied = new Dictionary<ProtoId<JobPrototype>, int>();
        foreach (var jobs in station.Comp.PlayerJobs.Values)
        {
            foreach (var job in jobs)
            {
                occupied.TryGetValue(job, out var count);
                occupied[job] = count + 1;
            }
        }

        var result = new Dictionary<ProtoId<JobPrototype>, JobCapacityState>();
        foreach (var (job, available) in station.Comp.JobList)
        {
            if (available is not { } vacancies)
                continue;

            occupied.TryGetValue(job, out var count);
            station.Comp.JobLimits.TryGetValue(job, out var limits);
            result.Add(job, GetJobCapacityState(vacancies, count, limits));
        }

        return result;
    }

    private static JobCapacityState GetJobCapacityState(int vacancies, int occupied, JobSlotLimits? limits)
    {
        var total = (long) vacancies + occupied;
        return new JobCapacityState(
            occupied,
            limits?.Min,
            limits?.Max,
            vacancies > 0 && (limits?.Min is not { } min || total > min),
            vacancies < int.MaxValue && (limits?.Max is not { } max || total < max));
    }
}
