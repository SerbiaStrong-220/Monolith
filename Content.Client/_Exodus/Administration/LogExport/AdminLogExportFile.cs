using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Exodus.Administration.LogExport;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Administration.LogExport;

/// <summary>
/// Bounded, byte-exact temporary storage. Cleanup waits for its outstanding disk operation.
/// </summary>
public sealed class AdminLogExportFile(Stream stream, Action delete, Action<ResPath>? publish = null) : IAsyncDisposable
{
    public static readonly ResPath ExportDirectory = new("/admin-log-exports");
    private Task _pendingIo = Task.CompletedTask;
    private Task? _cleanup;
    private Task? _closing;
    private bool _disposing;
    private bool _sealed;
    private bool _publishing;
    public long Bytes { get; private set; }
    public int Chunks { get; private set; }
    public int Lines { get; private set; }

    public static AdminLogExportFile Create(IWritableDirProvider storage)
    {
        storage.CreateDir(ExportDirectory);
        var path = ExportDirectory / $"{Guid.NewGuid():N}.tmp";
        var stream = storage.Open(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        return new AdminLogExportFile(stream, () => storage.Delete(path), destination => storage.Rename(path, destination));
    }

    public Task WriteAsync(int sequence, byte[] data, CancellationToken cancel)
    {
        if (_disposing || _sealed || !_pendingIo.IsCompleted || sequence != Chunks ||
            data.Length == 0 || data.Length > AdminLogExportLimits.ChunkBytes ||
            Bytes > AdminLogExportLimits.TotalBytes - data.Length)
            throw new InvalidDataException("Invalid export chunk.");

        return _pendingIo = WriteCoreAsync(data, cancel);
    }

    private async Task WriteCoreAsync(byte[] data, CancellationToken cancel)
    {
        await stream.WriteAsync(data.AsMemory(), cancel);
        cancel.ThrowIfCancellationRequested();
        Bytes += data.Length;
        Chunks++;
        foreach (var value in data)
        {
            if (value == (byte) '\n')
                Lines++;
        }
    }

    public Task CompleteAsync(AdminLogExportCompleted completed, int expectedLogs, CancellationToken cancel)
    {
        if (_disposing || _sealed || !_pendingIo.IsCompleted || completed.Bytes != Bytes ||
            completed.Chunks != Chunks || completed.Logs != expectedLogs ||
            Lines != (long) expectedLogs + 2 || stream.Length != Bytes)
            throw new InvalidDataException("Incomplete export.");

        _sealed = true;
        return _pendingIo = stream.FlushAsync(cancel);
    }

    public Task SaveAsync(ResPath destination, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (_disposing || !_sealed || _publishing || !_pendingIo.IsCompletedSuccessfully || publish == null)
            throw new InvalidOperationException("Export is not ready to save.");
        if (destination.Directory != ExportDirectory || destination.Extension != "jsonl")
            throw new ArgumentException("Export destination must be a JSONL file in the export directory.", nameof(destination));

        _publishing = true;
        return _pendingIo = SaveCoreAsync(destination, cancel);
    }

    private async Task SaveCoreAsync(ResPath destination, CancellationToken cancel)
    {
        await CloseStreamAsync();
        cancel.ThrowIfCancellationRequested();
        // Rename stays within one directory and fails if a destination already exists. A partial
        // download never receives a .jsonl name, including when closing/cancelling during disk IO.
        publish!(destination);
    }

    private Task CloseStreamAsync()
    {
        return _closing ??= stream.DisposeAsync().AsTask();
    }

    public ValueTask DisposeAsync()
    {
        _disposing = true;
        return new ValueTask(_cleanup ??= CleanupAsync());
    }

    private async Task CleanupAsync()
    {
        try
        {
            await _pendingIo;
        }
        catch (Exception)
        {
            // The operation owner handles the write/publish failure. Cleanup must still close and delete the temp.
        }

        try
        {
            await CloseStreamAsync();
        }
        finally
        {
            delete();
        }
    }
}
