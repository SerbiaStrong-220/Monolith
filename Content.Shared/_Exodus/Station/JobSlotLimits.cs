using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Station;

/// <summary>
/// Limits on the total number of occupied and vacant positions that a records console can configure.
/// Either bound may be omitted. These limits do not prevent players from taking or returning a vacancy.
/// </summary>
[DataDefinition]
public sealed partial class JobSlotLimits
{
    /// <summary>Minimum total positions. Null allows all vacant positions to be closed.</summary>
    [DataField]
    public int? Min;

    /// <summary>Maximum total positions. Null allows vacancies to be added without a configured cap.</summary>
    [DataField]
    public int? Max;
}

/// <summary>
/// A snapshot of a job's staffing limits and the adjustments currently allowed by the server.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct JobCapacityState(int Occupied, int? Min, int? Max, bool CanDecrease, bool CanIncrease);
