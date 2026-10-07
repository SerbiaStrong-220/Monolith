// (c) Space Exodus Team - EXDS-RL with CLA
using System.Text;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;

namespace Content.Server._Exodus.Administration.LogExport;

internal static class AdminLogExportDataBounds
{
    public const int MaxPageLogs = 256;
    public const int MaxRecordPlayers = 1024;
    public const int MaxPagePlayers = 4096;

    public static void ValidatePage(int roundId, int afterId, int lastLogId, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roundId);
        ArgumentOutOfRangeException.ThrowIfNegative(afterId);
        ArgumentOutOfRangeException.ThrowIfNegative(lastLogId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxPageLogs);
    }

    public static void ValidateRecordSize(int characters, int players)
    {
        if (characters > AdminLogExportLimits.RecordBytes || players > MaxRecordPlayers)
            throw new AdminLogExportDataException("admin-logs-export-error-record-too-large");
    }

    public static int GetMessageBytes(in SharedAdminLog log)
    {
        ValidateRecordSize(log.Message.Length, log.Players.Length);
        var bytes = Encoding.UTF8.GetByteCount(log.Message);
        if (bytes > AdminLogExportLimits.RecordBytes)
            throw new AdminLogExportDataException("admin-logs-export-error-record-too-large");

        return bytes;
    }
}
