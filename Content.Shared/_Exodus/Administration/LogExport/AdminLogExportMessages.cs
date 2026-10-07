using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Administration.LogExport;

/// <summary>
/// Bounds shared by both ends of the permission-checked, single-chunk export protocol.
/// Exceeding a bound fails the export; it must never produce a silently truncated success.
/// </summary>
public static class AdminLogExportLimits
{
    public const int ChunkBytes = 32 * 1024;
    public const int PageLogs = 128;
    public const int RecordBytes = 1024 * 1024;
    public const long TotalBytes = 512L * 1024 * 1024;
    public const int TotalLogs = 5_000_000;
    public const int ConcurrentExports = 2;
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ExportLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan UserCooldown = TimeSpan.FromMinutes(1);
}

[Serializable, NetSerializable]
public sealed class AdminLogExportBegin(int roundId) : EuiMessageBase
{
    public int RoundId { get; } = roundId;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportPrompt(Guid id, int roundId, int stage, string token) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public int RoundId { get; } = roundId;
    public int Stage { get; } = stage;
    public string Token { get; } = token;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportConfirm(Guid id, int stage, string token) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public int Stage { get; } = stage;
    public string Token { get; } = token;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportStarted(Guid id, int roundId, DateTime snapshotUtc, int totalLogs) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public int RoundId { get; } = roundId;
    public DateTime SnapshotUtc { get; } = snapshotUtc;
    public int TotalLogs { get; } = totalLogs;
}

/// <summary>Sequence is the next chunk requested, acknowledging that its predecessor reached disk.</summary>
[Serializable, NetSerializable]
public sealed class AdminLogExportNext(Guid id, int sequence) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public int Sequence { get; } = sequence;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportChunk(Guid id, int sequence, byte[] data) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public int Sequence { get; } = sequence;
    public byte[] Data { get; } = data;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportCompleted(Guid id, long bytes, int logs, int chunks) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public long Bytes { get; } = bytes;
    public int Logs { get; } = logs;
    public int Chunks { get; } = chunks;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportCancel(Guid id) : EuiMessageBase
{
    public Guid Id { get; } = id;
}

/// <summary>Client-reported local outcome, never proof that a remote file was actually written.</summary>
[Serializable, NetSerializable]
public sealed class AdminLogExportLocalResult(Guid id, bool saved) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public bool Saved { get; } = saved;
}

[Serializable, NetSerializable]
public sealed class AdminLogExportError(Guid id, string reason) : EuiMessageBase
{
    public Guid Id { get; } = id;
    public string Reason { get; } = reason;
}
