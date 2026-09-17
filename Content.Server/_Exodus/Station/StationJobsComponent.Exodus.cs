using Content.Shared._Exodus.Station;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Station.Components;

public sealed partial class StationJobsComponent
{
    /// <summary>
    /// Per-job bounds for manual staffing changes through records consoles on this station.
    /// Counts occupied positions as well as vacancies. Missing jobs or bounds have no configured limit.
    /// Initial positions are still configured by availableJobs.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, JobSlotLimits> JobLimits = new();
}
