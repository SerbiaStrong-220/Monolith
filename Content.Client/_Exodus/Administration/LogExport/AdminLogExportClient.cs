using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Eui;
using Robust.Shared.Utility;

namespace Content.Client._Exodus.Administration.LogExport;

/// <summary>
/// Owns a single explicitly confirmed export for an EUI, including across pop-out window changes.
/// Network callbacks may interleave while disk operations await, so every continuation checks its owner.
/// </summary>
public sealed class AdminLogExportClient(
    Func<AdminLogExportFile> createTemp,
    Action<EuiMessageBase> send,
    Action<Exception>? logError = null,
    Action? openFolder = null)
{
    public event Action? Changed;
    private Operation? _operation;
    private bool _closed;
    private bool _allowed;
    public bool Busy => _operation != null;
    public bool CanBegin => _allowed && !_closed && !Busy;
    public bool CanConfirm => _operation is { Phase: ExportPhase.Confirming, Cancelled: false } && _allowed && !_closed;
    public bool CanOpenFolder => !_closed && !Busy && SavedPath != null && openFolder != null;
    public ResPath? SavedPath { get; private set; }
    public int ConfirmationStage => _operation?.Stage ?? 0;
    public int RoundId => _operation?.Round ?? 0;
    public long BytesReceived => _operation?.File?.Bytes ?? 0;
    public int TotalLogs => _operation?.TotalLogs ?? 0;
    public string StatusKey { get; private set; } = "admin-logs-export-ready";

    public static bool IsExportMessage(EuiMessageBase message) => message is AdminLogExportPrompt or
        AdminLogExportStarted or AdminLogExportChunk or AdminLogExportCompleted or AdminLogExportError;

    public void SetPermission(bool allowed)
    {
        if (_closed || _allowed == allowed)
            return;

        _allowed = allowed;
        if (!allowed && _operation is { } operation)
            _ = StopAsync(operation, "admin-logs-export-error-permission");
        Notify();
    }

    public void Begin(int round)
    {
        if (!CanBegin || round <= 0)
            return;

        _operation = new Operation(round);
        SavedPath = null;
        StatusKey = "admin-logs-export-awaiting-confirmation";
        Notify();
        send(new AdminLogExportBegin(round));
    }

    public void Confirm()
    {
        if (!CanConfirm || _operation is not { } operation)
            return;

        operation.Confirmed = operation.Stage;
        operation.Phase = operation.Stage == 3 ? ExportPhase.AwaitingStart : ExportPhase.AwaitingPrompt;
        if (operation.Stage == 3)
            StatusKey = "admin-logs-export-preparing";
        Notify();
        send(new AdminLogExportConfirm(operation.Id, operation.Stage, operation.Token));
    }

    public Task CancelAsync()
    {
        return _operation is { } operation ? StopAsync(operation, "admin-logs-export-cancelled") : Task.CompletedTask;
    }

    public void OpenFolder()
    {
        if (!CanOpenFolder)
            return;
        try
        {
            openFolder!();
        }
        catch (Exception exception)
        {
            logError?.Invoke(exception);
            StatusKey = "admin-logs-export-error-open-folder";
            Notify();
        }
    }

    public Task CloseAsync()
    {
        _closed = true;
        Changed = null;
        return _operation is { } operation
            ? StopAsync(operation, "admin-logs-export-cancelled", sendCancel: false, force: true)
            : Task.CompletedTask;
    }

    public async Task HandleAsync(EuiMessageBase message)
    {
        if (_closed || _operation is not { } operation)
            return;

        try
        {
            switch (message)
            {
                case AdminLogExportPrompt prompt:
                    await HandlePromptAsync(operation, prompt);
                    break;
                case AdminLogExportStarted started when started.Id == operation.Id && started.RoundId == operation.Round:
                    if (!IsCurrent(operation) || operation.Phase != ExportPhase.AwaitingStart || operation.Confirmed != 3)
                        return;
                    if (started.TotalLogs < 0 || started.TotalLogs > AdminLogExportLimits.TotalLogs)
                    {
                        await StopAsync(operation, "admin-logs-export-error-too-large");
                        return;
                    }

                    operation.File = createTemp();
                    operation.TotalLogs = started.TotalLogs;
                    operation.Phase = ExportPhase.Downloading;
                    StatusKey = "admin-logs-export-downloading";
                    Notify();
                    send(new AdminLogExportNext(operation.Id, 0));
                    break;
                case AdminLogExportChunk chunk when chunk.Id == operation.Id:
                    await HandleChunkAsync(operation, chunk);
                    break;
                case AdminLogExportCompleted completed when completed.Id == operation.Id:
                    await HandleCompletedAsync(operation, completed);
                    break;
                case AdminLogExportError error when error.Id == operation.Id:
                    await StopAsync(operation, error.Reason, sendCancel: false, force: true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(operation))
                await StopAsync(operation, "admin-logs-export-cancelled");
        }
        catch (Exception exception)
        {
            logError?.Invoke(exception);
            if (IsCurrent(operation))
                await StopAsync(operation, exception is InvalidDataException
                    ? "admin-logs-export-error-incomplete"
                    : "admin-logs-export-error-local-io");
        }
    }

    private async Task HandlePromptAsync(Operation operation, AdminLogExportPrompt prompt)
    {
        if (prompt.RoundId != operation.Round || prompt.Id == Guid.Empty || prompt.Stage is < 1 or > 3 ||
            string.IsNullOrEmpty(prompt.Token) || prompt.Token.Length > 256)
            return;

        if (operation.Cancelled && operation.Id == Guid.Empty && prompt.Stage == 1)
        {
            operation.Id = prompt.Id;
            await StopAsync(operation, StatusKey, force: true);
            return;
        }

        if (!IsCurrent(operation) || operation.Phase != ExportPhase.AwaitingPrompt ||
            prompt.Stage != operation.Confirmed + 1 || (operation.Id != Guid.Empty && prompt.Id != operation.Id))
            return;

        operation.Id = prompt.Id;
        operation.Stage = prompt.Stage;
        operation.Token = prompt.Token;
        operation.Phase = ExportPhase.Confirming;
        Notify();
    }

    private async Task HandleChunkAsync(Operation operation, AdminLogExportChunk chunk)
    {
        if (!IsCurrent(operation) || operation.Phase != ExportPhase.Downloading || operation.File is not { } file)
            return;
        if (operation.Writing || chunk.Sequence != file.Chunks || chunk.Data.Length == 0 ||
            chunk.Data.Length > AdminLogExportLimits.ChunkBytes || file.Bytes > AdminLogExportLimits.TotalBytes - chunk.Data.Length)
        {
            await StopAsync(operation, "admin-logs-export-error-sequence");
            return;
        }

        operation.Writing = true;
        try
        {
            await file.WriteAsync(chunk.Sequence, chunk.Data, operation.Cancellation.Token);
        }
        finally
        {
            operation.Writing = false;
        }

        if (!IsCurrent(operation))
            return;
        Notify();
        send(new AdminLogExportNext(operation.Id, file.Chunks));
    }

    private async Task HandleCompletedAsync(Operation operation, AdminLogExportCompleted completed)
    {
        if (!IsCurrent(operation) || operation.Phase != ExportPhase.Downloading || operation.File is not { } file)
            return;
        if (operation.Writing)
        {
            await StopAsync(operation, "admin-logs-export-error-sequence");
            return;
        }

        operation.Phase = ExportPhase.Saving;
        await file.CompleteAsync(completed, operation.TotalLogs, operation.Cancellation.Token);
        if (!IsCurrent(operation))
            return;

        StatusKey = "admin-logs-export-saving";
        Notify();
        var destination = AdminLogExportFile.ExportDirectory / $"round-{operation.Round}-{Guid.NewGuid():N}.jsonl";
        await file.SaveAsync(destination, operation.Cancellation.Token);
        if (IsCurrent(operation))
        {
            SavedPath = destination;
            await CompleteLocalAsync(operation, true, "admin-logs-export-saved");
        }
    }

    private async Task CompleteLocalAsync(Operation operation, bool saved, string status)
    {
        if (!IsCurrent(operation))
            return;

        send(new AdminLogExportLocalResult(operation.Id, saved));
        await StopAsync(operation, status, sendCancel: false, force: true);
    }

    private async Task StopAsync(Operation operation, string status, bool sendCancel = true, bool force = false)
    {
        operation.Cancelled = true;
        operation.Cancellation.Cancel();
        if (ReferenceEquals(_operation, operation))
            StatusKey = status;
        if (sendCancel && !_closed && operation.Id != Guid.Empty && !operation.CancelSent)
        {
            operation.CancelSent = true;
            send(new AdminLogExportCancel(operation.Id));
        }

        Notify();
        // Begin has no client request id. Drain its first reply before allowing another same-round operation.
        if (!force && !_closed && operation.Id == Guid.Empty)
            return;

        try
        {
            if (operation.File != null)
                await operation.File.DisposeAsync();
        }
        catch (Exception exception)
        {
            logError?.Invoke(exception);
            if (ReferenceEquals(_operation, operation))
                StatusKey = "admin-logs-export-error-cleanup";
        }
        finally
        {
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                operation.Cancellation.Dispose();
                Notify();
            }
        }
    }

    private bool IsCurrent(Operation operation) => !_closed && _allowed && !operation.Cancelled && ReferenceEquals(_operation, operation);

    private void Notify()
    {
        if (!_closed)
            Changed?.Invoke();
    }

    private sealed class Operation(int round)
    {
        public readonly int Round = round;
        public readonly CancellationTokenSource Cancellation = new();
        public Guid Id;
        public int Stage;
        public int Confirmed;
        public string Token = string.Empty;
        public ExportPhase Phase;
        public int TotalLogs;
        public AdminLogExportFile? File;
        public bool Writing;
        public bool Cancelled;
        public bool CancelSent;
    }

    private enum ExportPhase : byte
    {
        AwaitingPrompt,
        Confirming,
        AwaitingStart,
        Downloading,
        Saving,
    }
}
