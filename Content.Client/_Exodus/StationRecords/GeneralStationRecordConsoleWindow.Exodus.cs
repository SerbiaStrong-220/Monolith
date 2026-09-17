using Content.Client._NF.StationRecords;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.Prototypes;

namespace Content.Client.StationRecords;

public sealed partial class GeneralStationRecordConsoleWindow
{
    private static void ApplyJobCapacity(JobRow row, ProtoId<JobPrototype> job, GeneralStationRecordConsoleState state)
    {
        if (state.JobCapacity == null || !state.JobCapacity.TryGetValue(job, out var capacity))
            return;

        row.DecreaseJobSlot.Disabled = !capacity.CanDecrease;
        row.IncreaseJobSlot.Disabled = !capacity.CanIncrease;
        row.ToolTip = Loc.GetString("exodus-station-records-job-capacity",
            ("occupied", capacity.Occupied),
            ("min", capacity.Min?.ToString() ?? Loc.GetString("exodus-station-records-job-no-limit")),
            ("max", capacity.Max?.ToString() ?? Loc.GetString("exodus-station-records-job-no-limit")));
        row.JobAmount.ToolTip = row.ToolTip;
    }
}
