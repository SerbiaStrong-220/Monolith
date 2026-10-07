// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>
/// A fixed boundary for one round's export without copying its complete log collection.
/// </summary>
public sealed record AdminLogExportSnapshot(int RoundId, int Count, int LastLogId, DateTime CreatedAtUtc)
{
    /// <summary>
    /// Identity of the live cache list. Cached exports use Count and an insertion-order offset:
    /// their LastLogId is the final captured entry, not a database upper bound or ordering guarantee.
    /// </summary>
    internal object? CacheIdentity { get; init; }
}
