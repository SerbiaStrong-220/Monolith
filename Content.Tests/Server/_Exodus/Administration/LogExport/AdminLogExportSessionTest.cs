using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Server.Administration.Logs;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Eui;
using Moq;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests.Server._Exodus.Administration.LogExport;

[TestFixture]
public sealed class AdminLogExportSessionTest
{
    private readonly DateTime _date = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private Mock<IAdminLogManager> _logs;
    private AdminLogExportSystem _exports;
    private AdminLogExportSession _session;
    private List<EuiMessageBase> _sent;
    private List<string> _audit;
    private bool _allowed;
    private TimeSpan _now;
    private NetUserId _user;

    [SetUp]
    public void SetUp()
    {
        _logs = new Mock<IAdminLogManager>(MockBehavior.Strict);
        _exports = new AdminLogExportSystem();
        _sent = [];
        _audit = [];
        _allowed = true;
        _now = TimeSpan.Zero;
        _user = new NetUserId(Guid.NewGuid());
        _session = new AdminLogExportSession(_user, _logs.Object, _exports, () => _now,
            () => _allowed, () => 42, _sent.Add, _audit.Add);
    }

    [Test]
    public async Task NoDataAccessUntilThreeConfirmationsAndRoundStaysCaptured()
    {
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminLogExportSnapshot(8, 0, 0, _date));
        await _session.HandleMessageAsync(new AdminLogExportBegin(8));
        var first = LastPrompt();
        await Confirm(first);
        await Confirm(LastPrompt());
        _logs.Verify(l => l.CreateExportSnapshotAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_sent.OfType<AdminLogExportStarted>(), Is.Empty);
        await Confirm(LastPrompt());
        Assert.That(_sent.OfType<AdminLogExportStarted>().Single().RoundId, Is.EqualTo(8));
        Assert.That(_exports.ActiveCount, Is.EqualTo(1));
        Assert.That(_audit.Single(), Does.Contain("requested").And.Contain(_user.ToString()).And.Contain("round=8"));
    }

    [Test]
    public async Task UnauthorizedAndOutOfRangeRequestsCannotReadData()
    {
        _allowed = false;
        await _session.HandleMessageAsync(new AdminLogExportBegin(8));
        Assert.That(_sent.OfType<AdminLogExportError>().Last().Reason, Is.EqualTo("admin-logs-export-error-permission"));
        _allowed = true;
        await _session.HandleMessageAsync(new AdminLogExportBegin(0));
        await _session.HandleMessageAsync(new AdminLogExportBegin(43));
        Assert.That(_sent.OfType<AdminLogExportPrompt>(), Is.Empty);
        Assert.That(_exports.ActiveCount, Is.Zero);
        _logs.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelOrPermissionRevocationDuringSnapshotRetainsLeaseUntilIoFinishes(bool revoke)
    {
        var completion = new TaskCompletionSource<AdminLogExportSnapshot>();
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>())).Returns(completion.Task);
        await BeginAndConfirmTwice();
        var final = LastPrompt();
        var pending = Confirm(final);
        Assert.That(pending.IsCompleted, Is.False);
        if (revoke)
        {
            _allowed = false;
            _session.PermissionsChanged();
        }
        else
            await _session.HandleMessageAsync(new AdminLogExportCancel(final.Id));

        Assert.That(_exports.ActiveCount, Is.EqualTo(1));
        completion.SetResult(new AdminLogExportSnapshot(8, 1, 1, _date));
        await pending;
        Assert.That(_exports.ActiveCount, Is.Zero);
        Assert.That(_sent.OfType<AdminLogExportStarted>(), Is.Empty);
        Assert.That(_sent.OfType<AdminLogExportChunk>(), Is.Empty);
        Assert.That(_audit.Last(), Does.Contain(revoke ? "permission" : "cancel"));
    }

    [Test]
    public async Task RevocationAfterPageReadCannotLeakChunk()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<SharedAdminLog>>();
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminLogExportSnapshot(8, 1, 1, _date));
        _logs.Setup(l => l.ReadExportPageAsync(It.IsAny<AdminLogExportSnapshot>(), 0, 0,
            AdminLogExportLimits.PageLogs, It.IsAny<CancellationToken>())).Returns(completion.Task);
        await BeginAndConfirmTwice();
        var final = LastPrompt();
        await Confirm(final);
        var pending = _session.HandleMessageAsync(new AdminLogExportNext(final.Id, 0));
        _allowed = false;
        completion.SetResult([new SharedAdminLog(1, LogType.AdminCommands, LogImpact.Low, _date, "private", [])]);
        await pending;
        Assert.That(_sent.OfType<AdminLogExportChunk>(), Is.Empty);
        Assert.That(_exports.ActiveCount, Is.Zero);
    }

    [Test]
    public async Task DuplicatePullCancelsAndSuccessfulTransferRequiresFinalAcknowledgement()
    {
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminLogExportSnapshot(8, 0, 0, _date));
        await BeginAndConfirmTwice();
        var id = LastPrompt().Id;
        await Confirm(LastPrompt());
        await _session.HandleMessageAsync(new AdminLogExportNext(id, 0));
        Assert.That(_sent.OfType<AdminLogExportChunk>().Single().Sequence, Is.Zero);
        Assert.That(_sent.OfType<AdminLogExportCompleted>(), Is.Empty);
        await _session.HandleMessageAsync(new AdminLogExportNext(id, 0));
        Assert.That(_sent.OfType<AdminLogExportError>().Last().Reason, Is.EqualTo("admin-logs-export-error-sequence"));
        Assert.That(_exports.ActiveCount, Is.Zero);

        _now = AdminLogExportLimits.UserCooldown;
        await BeginAndConfirmTwice();
        id = LastPrompt().Id;
        await Confirm(LastPrompt());
        await _session.HandleMessageAsync(new AdminLogExportNext(id, 0));
        await _session.HandleMessageAsync(new AdminLogExportNext(id, 1));
        var done = _sent.OfType<AdminLogExportCompleted>().Single();
        Assert.That(done.Logs, Is.Zero);
        Assert.That(done.Chunks, Is.EqualTo(1));
        Assert.That(done.Bytes, Is.EqualTo(_sent.OfType<AdminLogExportChunk>().Last().Data.Length));
        Assert.That(_exports.ActiveCount, Is.Zero);
        await _session.HandleMessageAsync(new AdminLogExportLocalResult(id, true));
        Assert.That(_audit.Last(), Does.Contain("client-reported saved"));
    }

    [Test]
    public async Task SecondPullWhileIoIsPendingCannotStartAnotherReadOrFreeItsLease()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<SharedAdminLog>>();
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminLogExportSnapshot(8, 1, 1, _date));
        _logs.Setup(l => l.ReadExportPageAsync(It.IsAny<AdminLogExportSnapshot>(), 0, 0,
            AdminLogExportLimits.PageLogs, It.IsAny<CancellationToken>())).Returns(completion.Task);
        await BeginAndConfirmTwice();
        var id = LastPrompt().Id;
        await Confirm(LastPrompt());
        var pending = _session.HandleMessageAsync(new AdminLogExportNext(id, 0));
        await _session.HandleMessageAsync(new AdminLogExportNext(id, 1));
        Assert.That(_exports.ActiveCount, Is.EqualTo(1));
        Assert.That(_sent.OfType<AdminLogExportError>().Last().Reason, Is.EqualTo("admin-logs-export-error-sequence"));
        completion.SetResult([new SharedAdminLog(1, LogType.AdminCommands, LogImpact.Low, _date, "private", [])]);
        await pending;
        Assert.That(_exports.ActiveCount, Is.Zero);
        Assert.That(_sent.OfType<AdminLogExportChunk>(), Is.Empty);
        _logs.Verify(l => l.ReadExportPageAsync(It.IsAny<AdminLogExportSnapshot>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ClosedWindowDoesNotSendLateSnapshot()
    {
        var completion = new TaskCompletionSource<AdminLogExportSnapshot>();
        _logs.Setup(l => l.CreateExportSnapshotAsync(8, It.IsAny<CancellationToken>())).Returns(completion.Task);
        await BeginAndConfirmTwice();
        var pending = Confirm(LastPrompt());
        _session.Close();
        var sentBefore = _sent.Count;
        completion.SetResult(new AdminLogExportSnapshot(8, 0, 0, _date));
        await pending;
        Assert.That(_sent.Count, Is.EqualTo(sentBefore));
        Assert.That(_exports.ActiveCount, Is.Zero);
    }

    private AdminLogExportPrompt LastPrompt() => _sent.OfType<AdminLogExportPrompt>().Last();

    private Task Confirm(AdminLogExportPrompt prompt) =>
        _session.HandleMessageAsync(new AdminLogExportConfirm(prompt.Id, prompt.Stage, prompt.Token));

    private async Task BeginAndConfirmTwice()
    {
        await _session.HandleMessageAsync(new AdminLogExportBegin(8));
        await Confirm(LastPrompt());
        await Confirm(LastPrompt());
    }
}
