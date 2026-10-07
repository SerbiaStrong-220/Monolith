using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Eui;
using Robust.Shared.Network;

namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>
/// One authenticated EUI's export state. All entry points and continuations run on the server thread.
/// Cancelling an outstanding read keeps its global lease until that read has actually returned.
/// </summary>
public sealed class AdminLogExportSession(
    NetUserId user,
    IAdminLogManager logs,
    AdminLogExportSystem exports,
    Func<TimeSpan> now,
    Func<bool> canExport,
    Func<int> currentRound,
    Action<EuiMessageBase> send,
    Action<string> audit)
{
    private AdminLogExportChallenge? _challenge;
    private Job? _job;
    private CompletedTransfer? _completed;
    private bool _closed;

    private sealed class Job(Guid id, int roundId)
    {
        public readonly Guid Id = id;
        public readonly int RoundId = roundId;
        public readonly CancellationTokenSource Cancellation = new();
        public AdminLogExportSystem.Lease Lease = default!;
        public AdminLogExportSnapshot? Snapshot;
        public AdminLogJsonlProducer? Producer;
        public bool InFlight;
        public bool Cancelled;
        public int Sequence;
        public long Bytes;
    }

    private sealed record CompletedTransfer(Guid Id, int RoundId, long Bytes, int Logs, int Chunks);

    public static bool IsExportMessage(EuiMessageBase message) => message is
        AdminLogExportBegin or AdminLogExportConfirm or AdminLogExportNext or
        AdminLogExportCancel or AdminLogExportLocalResult;

    public async Task HandleMessageAsync(EuiMessageBase message)
    {
        if (_closed || !IsExportMessage(message))
            return;

        if (!canExport())
        {
            var hadOperation = _job != null || _challenge != null;
            PermissionsChanged();
            if (!hadOperation)
                Reject(Guid.Empty, 0, "admin-logs-export-error-permission");
            return;
        }

        switch (message)
        {
            case AdminLogExportBegin begin:
                Begin(begin.RoundId);
                break;
            case AdminLogExportConfirm confirm:
                await ConfirmAsync(confirm);
                break;
            case AdminLogExportNext next:
                await PullAsync(next);
                break;
            case AdminLogExportCancel cancel:
                if (_challenge?.Id == cancel.Id)
                    CancelChallenge("admin-logs-export-error-cancelled");
                if (_job?.Id == cancel.Id)
                    CancelJob(_job, "admin-logs-export-error-cancelled");
                if (_completed?.Id == cancel.Id)
                    ReportLocalResult(false);
                break;
            case AdminLogExportLocalResult result when _completed?.Id == result.Id:
                ReportLocalResult(result.Saved);
                break;
        }
    }

    public void PermissionsChanged()
    {
        if (canExport())
            return;

        CancelChallenge("admin-logs-export-error-permission");
        if (_job != null)
            CancelJob(_job, "admin-logs-export-error-permission");
        if (_completed != null)
        {
            Audit("local outcome unavailable: permission revoked", _completed.Id, _completed.RoundId,
                _completed.Bytes, _completed.Logs);
            _completed = null;
        }
    }

    public void Close()
    {
        _closed = true;
        CancelChallenge("admin-logs-export-error-cancelled");
        if (_job != null)
            CancelJob(_job, "admin-logs-export-error-cancelled");
        if (_completed != null)
        {
            Audit("local outcome unavailable: window closed", _completed.Id, _completed.RoundId,
                _completed.Bytes, _completed.Logs);
            _completed = null;
        }
    }

    private void Begin(int roundId)
    {
        if (_job != null)
        {
            Reject(Guid.Empty, roundId, "admin-logs-export-error-busy");
            return;
        }

        if (roundId <= 0 || roundId > currentRound())
        {
            Reject(Guid.Empty, roundId, "admin-logs-export-error-invalid-round");
            return;
        }

        if (_completed != null)
        {
            Audit("local outcome unavailable: another export started", _completed.Id, _completed.RoundId,
                _completed.Bytes, _completed.Logs);
            _completed = null;
        }

        CancelChallenge("admin-logs-export-error-cancelled");
        _challenge = new AdminLogExportChallenge(roundId, now());
        SendPrompt();
    }

    private async Task ConfirmAsync(AdminLogExportConfirm confirm)
    {
        var challenge = _challenge;
        if (challenge == null)
        {
            if (_job?.Id == confirm.Id)
                CancelJob(_job, "admin-logs-export-error-confirmation");
            else
                Reject(confirm.Id, 0, "admin-logs-export-error-confirmation");
            return;
        }

        if (!challenge.TryConfirm(confirm.Id, confirm.Stage, confirm.Token, now(), out var complete))
        {
            _challenge = null;
            Reject(challenge.Id, challenge.RoundId, "admin-logs-export-error-confirmation");
            return;
        }

        if (!complete)
        {
            SendPrompt();
            return;
        }

        _challenge = null;
        var job = new Job(challenge.Id, challenge.RoundId);
        if (!exports.TryAcquire(user, job.Id, now(), error => CancelJob(job, error), out job.Lease, out var error))
        {
            job.Cancellation.Dispose();
            Reject(job.Id, job.RoundId, error);
            return;
        }

        _job = job;
        job.InFlight = true;
        Audit("requested", job.Id, job.RoundId, 0, 0);
        try
        {
            job.Snapshot = await logs.CreateExportSnapshotAsync(job.RoundId, job.Cancellation.Token);
            if (!CheckContinuation(job))
                return;

            if (job.Snapshot == null || job.Snapshot.RoundId != job.RoundId)
            {
                CancelJob(job, "admin-logs-export-error-invalid-round");
                return;
            }

            job.Producer = new AdminLogJsonlProducer(job.RoundId, job.Snapshot.Count,
                job.Snapshot.LastLogId, job.Snapshot.CreatedAtUtc);
            send(new AdminLogExportStarted(job.Id, job.RoundId, job.Snapshot.CreatedAtUtc, job.Snapshot.Count));
            exports.Touch(job.Lease, now());
        }
        catch (OperationCanceledException)
        {
            CancelJob(job, "admin-logs-export-error-cancelled");
        }
        catch (AdminLogExportDataException exception)
        {
            CancelJob(job, exception.ErrorKey);
        }
        catch (Exception)
        {
            // Never include exception text: providers can embed log contents or database details in it.
            CancelJob(job, "admin-logs-export-error-read");
        }
        finally
        {
            job.InFlight = false;
            if (job.Cancelled)
                Release(job);
        }
    }

    private async Task PullAsync(AdminLogExportNext next)
    {
        var job = _job;
        if (job == null || next.Id != job.Id)
        {
            Reject(next.Id, 0, "admin-logs-export-error-sequence");
            return;
        }

        if (job.Cancelled)
            return;

        if (job.InFlight || next.Sequence != job.Sequence || job.Producer == null || job.Snapshot == null)
        {
            CancelJob(job, "admin-logs-export-error-sequence");
            return;
        }

        if (!CheckContinuation(job))
            return;

        exports.Touch(job.Lease, now());
        job.InFlight = true;
        try
        {
            var data = await job.Producer.ReadChunkAsync(async (offset, afterId, limit, cancellation) =>
            {
                var page = await logs.ReadExportPageAsync(job.Snapshot, offset, afterId, limit, cancellation);
                if (!CheckContinuation(job))
                    throw new OperationCanceledException();
                return page;
            }, job.Cancellation.Token);
            if (!CheckContinuation(job))
                return;

            if (data != null)
            {
                var sequence = job.Sequence++;
                job.Bytes += data.Length;
                send(new AdminLogExportChunk(job.Id, sequence, data));
                exports.Touch(job.Lease, now());
                return;
            }

            // Null is reached only on the pull acknowledging the already delivered final footer.
            _completed = new CompletedTransfer(job.Id, job.RoundId, job.Bytes, job.Snapshot.Count, job.Sequence);
            Audit("transfer completed", job.Id, job.RoundId, job.Bytes, job.Snapshot.Count);
            send(new AdminLogExportCompleted(job.Id, job.Bytes, job.Snapshot.Count, job.Sequence));
            Release(job);
        }
        catch (OperationCanceledException)
        {
            CancelJob(job, "admin-logs-export-error-cancelled");
        }
        catch (AdminLogExportDataException exception)
        {
            CancelJob(job, exception.ErrorKey);
        }
        catch (Exception)
        {
            CancelJob(job, "admin-logs-export-error-read");
        }
        finally
        {
            job.InFlight = false;
            if (job.Cancelled)
                Release(job);
        }
    }

    private bool CheckContinuation(Job job)
    {
        if (job.Cancelled || _closed || !ReferenceEquals(_job, job))
            return false;

        if (!canExport())
        {
            CancelJob(job, "admin-logs-export-error-permission");
            return false;
        }

        if (now() - job.Lease.Started >= AdminLogExportLimits.ExportLifetime ||
            now() - job.Lease.LastActivity >= AdminLogExportLimits.IdleTimeout)
        {
            CancelJob(job, "admin-logs-export-error-timeout");
            return false;
        }

        return true;
    }

    private void CancelChallenge(string reason)
    {
        if (_challenge == null)
            return;

        var challenge = _challenge;
        _challenge = null;
        challenge.Cancel();
        Reject(challenge.Id, challenge.RoundId, reason);
    }

    private void CancelJob(Job job, string reason)
    {
        if (job.Cancelled)
            return;

        job.Cancelled = true;
        job.Cancellation.Cancel();
        Audit($"failed/cancelled: {reason}", job.Id, job.RoundId, job.Bytes, job.Snapshot?.Count ?? 0);
        if (!_closed)
            send(new AdminLogExportError(job.Id, reason));
        if (!job.InFlight)
            Release(job);
    }

    private void Release(Job job)
    {
        exports.Release(job.Lease);
        if (!ReferenceEquals(_job, job))
            return;

        _job = null;
        job.Producer = null;
        job.Snapshot = null;
        job.Cancellation.Dispose();
    }

    private void SendPrompt()
    {
        if (_challenge is { } challenge)
            send(new AdminLogExportPrompt(challenge.Id, challenge.RoundId, challenge.Stage, challenge.Token));
    }

    private void Reject(Guid id, int roundId, string reason)
    {
        Audit($"failed/cancelled: {reason}", id, roundId, 0, 0);
        if (!_closed)
            send(new AdminLogExportError(id, reason));
    }

    private void ReportLocalResult(bool saved)
    {
        var completed = _completed!;
        _completed = null;
        Audit(saved ? "client-reported saved" : "client-reported save cancelled/failed",
            completed.Id, completed.RoundId, completed.Bytes, completed.Logs);
    }

    private void Audit(string outcome, Guid id, int roundId, long bytes, int count)
    {
        audit($"Admin round log export {outcome}: user={user}; round={roundId}; export={id}; logs={count}; bytes={bytes}");
    }
}
