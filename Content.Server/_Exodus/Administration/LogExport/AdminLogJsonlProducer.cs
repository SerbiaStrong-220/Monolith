using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;

namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>Produces bounded JSONL chunks with only one page and one serialized record retained.</summary>
public sealed class AdminLogJsonlProducer
{
    private readonly int _totalLogs;
    private readonly int _recordLimit;
    private readonly long _totalLimit;
    private IReadOnlyList<SharedAdminLog>? _page;
    private int _pageIndex;
    private byte[]? _record;
    private int _recordOffset;
    private int _logs;
    private int _afterId;
    private long _bytes;
    private bool _footer;

    public AdminLogJsonlProducer(int roundId, int totalLogs, int lastLogId, DateTime snapshotUtc,
        int recordLimit = AdminLogExportLimits.RecordBytes, long totalLimit = AdminLogExportLimits.TotalBytes)
    {
        if (totalLogs < 0 || totalLogs > AdminLogExportLimits.TotalLogs)
            throw new AdminLogExportDataException("admin-logs-export-error-too-large");

        _totalLogs = totalLogs;
        _recordLimit = recordLimit;
        _totalLimit = totalLimit;
        SetRecord(new { kind = "round_metadata", roundId, snapshotUtc, totalLogs, lastLogId });
    }

    public async Task<byte[]?> ReadChunkAsync(
        Func<int, int, int, CancellationToken, Task<IReadOnlyList<SharedAdminLog>>> readPage,
        CancellationToken cancellation)
    {
        var chunk = new byte[AdminLogExportLimits.ChunkBytes];
        var length = 0;
        while (length < chunk.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_record == null)
            {
                if (_logs == _totalLogs)
                {
                    if (_footer)
                        break;

                    SetRecord(new { kind = "complete", logs = _logs });
                    _footer = true;
                }
                else
                {
                    if (_page == null || _pageIndex == _page.Count)
                    {
                        _page = null;
                        _page = await readPage(_logs, _afterId, AdminLogExportLimits.PageLogs, cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        _pageIndex = 0;
                        if (_page.Count == 0 || _page.Count > AdminLogExportLimits.PageLogs ||
                            _page.Count > _totalLogs - _logs)
                            throw new AdminLogExportDataException("admin-logs-export-error-incomplete");
                    }

                    var log = _page[_pageIndex++];
                    // The data source bounds pages before materialization; these checks also guard custom sources.
                    if (log.Message.Length > _recordLimit || Encoding.UTF8.GetByteCount(log.Message) > _recordLimit ||
                        log.Players.Length > AdminLogExportDataBounds.MaxRecordPlayers)
                        throw new AdminLogExportDataException("admin-logs-export-error-record-too-large");

                    SetRecord(new
                    {
                        kind = "log",
                        id = log.Id,
                        type = log.Type,
                        impact = log.Impact,
                        date = log.Date,
                        message = log.Message,
                        players = log.Players
                    });
                    _afterId = log.Id;
                    _logs++;
                }
            }

            var copy = Math.Min(chunk.Length - length, _record!.Length - _recordOffset);
            if (_bytes + copy > _totalLimit)
                throw new AdminLogExportDataException("admin-logs-export-error-too-large");

            Array.Copy(_record, _recordOffset, chunk, length, copy);
            length += copy;
            _bytes += copy;
            _recordOffset += copy;
            if (_recordOffset == _record.Length)
                _record = null;
        }

        if (length == 0)
            return null;

        if (length != chunk.Length)
            Array.Resize(ref chunk, length);
        return chunk;
    }

    private void SetRecord<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        if (json.Length >= _recordLimit)
            throw new AdminLogExportDataException("admin-logs-export-error-record-too-large");

        Array.Resize(ref json, json.Length + 1);
        json[^1] = (byte) '\n';
        _record = json;
        _recordOffset = 0;
    }
}
