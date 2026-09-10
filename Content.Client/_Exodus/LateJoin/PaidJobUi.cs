using Content.Shared._NF.Bank;
using Content.Shared.Roles;

namespace Content.Client._Exodus.LateJoin;

public static class PaidJobUi
{
    public static string GetName(JobPrototype job, string slotCount)
    {
        var name = job.LocalizedName + slotCount;
        return job.EntryPrice > 0
            ? Loc.GetString("paid-job-name", ("job", name), ("price", BankSystemExtensions.ToSpesoString(job.EntryPrice)))
            : name;
    }

    public static string GetTooltip(JobPrototype job, string reason)
    {
        if (job.EntryPrice <= 0)
            return reason;

        var description = Loc.GetString("paid-job-tooltip", ("price", BankSystemExtensions.ToSpesoString(job.EntryPrice)));
        return string.IsNullOrEmpty(reason) ? description : $"{reason}\n{description}";
    }
}
