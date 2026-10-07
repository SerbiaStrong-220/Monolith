using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests.Server._Exodus.Administration.LogExport;

[TestFixture]
public sealed class AdminLogExportSecurityTest
{
    [Test]
    public void ConfirmationBindsRoundAndRequiresThreeFreshTokens()
    {
        var challenge = new AdminLogExportChallenge(42, TimeSpan.Zero);
        var first = challenge.Token;
        Assert.That(challenge.RoundId, Is.EqualTo(42));
        Assert.That(challenge.TryConfirm(challenge.Id, 3, first, TimeSpan.Zero, out _), Is.False);
        Assert.That(challenge.TryConfirm(challenge.Id, 1, first, TimeSpan.Zero, out _), Is.False,
            "An invalid confirmation invalidates the challenge.");

        challenge = new AdminLogExportChallenge(42, TimeSpan.Zero);
        first = challenge.Token;
        Assert.That(challenge.TryConfirm(challenge.Id, 1, first, TimeSpan.Zero, out var complete), Is.True);
        Assert.That(complete, Is.False);
        Assert.That(challenge.Token, Is.Not.EqualTo(first));
        Assert.That(challenge.TryConfirm(challenge.Id, 2, challenge.Token, TimeSpan.Zero, out complete), Is.True);
        Assert.That(complete, Is.False);
        var final = challenge.Token;
        Assert.That(challenge.TryConfirm(challenge.Id, 3, final, TimeSpan.Zero, out complete), Is.True);
        Assert.That(complete, Is.True);
        Assert.That(challenge.TryConfirm(challenge.Id, 3, final, TimeSpan.Zero, out _), Is.False);
    }

    [Test]
    public void ExpiredCancelledAndOtherWindowConfirmationsFail()
    {
        var challenge = new AdminLogExportChallenge(8, TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(Guid.NewGuid(), 1, challenge.Token, TimeSpan.Zero, out _), Is.False);
        challenge = new AdminLogExportChallenge(8, TimeSpan.Zero);
        Assert.That(challenge.TryConfirm(challenge.Id, 1, challenge.Token,
            AdminLogExportLimits.ConfirmationLifetime, out _), Is.False);
        challenge = new AdminLogExportChallenge(8, TimeSpan.Zero);
        challenge.Cancel();
        Assert.That(challenge.TryConfirm(challenge.Id, 1, challenge.Token, TimeSpan.Zero, out _), Is.False);
    }

    [Test]
    public void GlobalLeasesLimitUsersAndKeepCancelledIoReservedUntilRelease()
    {
        var exports = new AdminLogExportSystem();
        var user = new NetUserId(Guid.NewGuid());
        string cancellation = null;
        Assert.That(exports.TryAcquire(user, Guid.NewGuid(), TimeSpan.Zero, e => cancellation = e,
            out var first, out _), Is.True);
        Assert.That(exports.TryAcquire(user, Guid.NewGuid(), TimeSpan.Zero, _ => {}, out _, out _), Is.False);
        Assert.That(exports.TryAcquire(new NetUserId(Guid.NewGuid()), Guid.NewGuid(), TimeSpan.Zero,
            _ => {}, out var second, out _), Is.True);
        Assert.That(exports.TryAcquire(new NetUserId(Guid.NewGuid()), Guid.NewGuid(), TimeSpan.Zero,
            _ => {}, out _, out _), Is.False);

        exports.Expire(AdminLogExportLimits.IdleTimeout);
        Assert.That(cancellation, Is.EqualTo("admin-logs-export-error-timeout"));
        Assert.That(exports.ActiveCount, Is.EqualTo(2), "Cancellation must not free a slot while DB IO can still run.");
        exports.Release(first);
        exports.Release(second);
        Assert.That(exports.ActiveCount, Is.Zero);
        Assert.That(exports.TryAcquire(user, Guid.NewGuid(), TimeSpan.FromSeconds(1), _ => {},
            out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("admin-logs-export-error-cooldown"));
        Assert.That(exports.TryAcquire(user, Guid.NewGuid(), AdminLogExportLimits.UserCooldown, _ => {},
            out _, out _), Is.True);
    }

    [Test]
    public void ActiveRequestsCannotExtendTheAbsoluteLifetime()
    {
        var exports = new AdminLogExportSystem();
        string cancellation = null;
        Assert.That(exports.TryAcquire(new NetUserId(Guid.NewGuid()), Guid.NewGuid(), TimeSpan.Zero,
            error => cancellation = error, out var lease, out _), Is.True);
        exports.Touch(lease, AdminLogExportLimits.ExportLifetime - TimeSpan.FromSeconds(1));
        exports.Expire(AdminLogExportLimits.ExportLifetime);
        Assert.That(cancellation, Is.EqualTo("admin-logs-export-error-timeout"));
        Assert.That(exports.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task JsonLinesEscapeMessagesAndKeepExactFramingAcrossChunkBoundaries()
    {
        var player = Guid.NewGuid();
        var message = "строка\n\"quoted\"\\\t" + new string('x', AdminLogExportLimits.ChunkBytes * 2);
        var date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[] { new SharedAdminLog(10, LogType.AdminCommands, LogImpact.High, date, message, [player]) };
        var writer = new AdminLogJsonlProducer(42, 1, 10, date);
        using var output = new MemoryStream();
        var reads = 0;
        while (await writer.ReadChunkAsync((offset, after, limit, _) =>
        {
            reads++;
            Assert.That(offset, Is.Zero);
            Assert.That(limit, Is.LessThanOrEqualTo(AdminLogExportLimits.PageLogs));
            return Task.FromResult<IReadOnlyList<SharedAdminLog>>(rows);
        }, CancellationToken.None) is { } chunk)
        {
            Assert.That(chunk.Length, Is.InRange(1, AdminLogExportLimits.ChunkBytes));
            output.Write(chunk);
        }

        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n');
        Assert.That(lines.Length, Is.EqualTo(4));
        using var header = JsonDocument.Parse(lines[0]);
        using var log = JsonDocument.Parse(lines[1]);
        using var footer = JsonDocument.Parse(lines[2]);
        Assert.Multiple(() =>
        {
            Assert.That(header.RootElement.GetProperty("kind").GetString(), Is.EqualTo("round_metadata"));
            Assert.That(header.RootElement.GetProperty("roundId").GetInt32(), Is.EqualTo(42));
            Assert.That(log.RootElement.GetProperty("message").GetString(), Is.EqualTo(message));
            Assert.That(log.RootElement.GetProperty("players")[0].GetGuid(), Is.EqualTo(player));
            Assert.That(log.RootElement.GetProperty("id").GetInt32(), Is.EqualTo(10));
            Assert.That(log.RootElement.GetProperty("date").GetDateTime(), Is.EqualTo(date));
            Assert.That(footer.RootElement.GetProperty("kind").GetString(), Is.EqualTo("complete"));
            Assert.That(footer.RootElement.GetProperty("logs").GetInt32(), Is.EqualTo(1));
            Assert.That(reads, Is.EqualTo(1));
        });
    }

    [Test]
    public void MissingRowsAndByteLimitsFailExplicitly()
    {
        var date = DateTime.UtcNow;
        var missing = new AdminLogJsonlProducer(1, 1, 1, date);
        var error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await missing.ReadChunkAsync((_, _, _, _) =>
                Task.FromResult<IReadOnlyList<SharedAdminLog>>([]), CancellationToken.None));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-incomplete"));

        var huge = new AdminLogJsonlProducer(1, 1, 1, date, recordLimit: 512);
        error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await huge.ReadChunkAsync((_, _, _, _) => Task.FromResult<IReadOnlyList<SharedAdminLog>>(
                [new SharedAdminLog(1, LogType.AdminCommands, LogImpact.Low, date, new string('x', 1024), [])]),
                CancellationToken.None));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-record-too-large"));

        var total = new AdminLogJsonlProducer(1, 0, 0, date, totalLimit: 10);
        error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await total.ReadChunkAsync((_, _, _, _) => throw new AssertionException("No rows expected"),
                CancellationToken.None));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-too-large"));
    }
}
