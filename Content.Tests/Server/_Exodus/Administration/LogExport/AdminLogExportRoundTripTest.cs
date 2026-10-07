// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._Exodus.Administration.LogExport;
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
public sealed class AdminLogExportRoundTripTest
{
    [Test]
    public async Task ThreeConfirmationsSaveEverySelectedRoundLogAcrossPagesAndChunks()
    {
        const int selectedRound = 8;
        const int currentRound = 42;
        var date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var firstPlayer = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondPlayer = Guid.Parse("20000000-0000-0000-0000-000000000002");
        // Mix all displayed fields so exporting only one type, impact, player or search match loses records.
        var expected = Enumerable.Range(0, 257).Select(index => new SharedAdminLog(
            index * 3 + 1,
            index % 2 == 0 ? LogType.Unknown : LogType.AdminCommands,
            index % 2 == 0 ? LogImpact.Low : LogImpact.High,
            date.AddTicks(index * 1001),
            $"Запись {index}: \"станция\"\\сектор\nВторая строка {new string('я', 150)}",
            index % 3 == 0 ? [] : index % 3 == 1 ? [firstPlayer] : [secondPlayer, firstPlayer])).ToArray();
        var rounds = new Dictionary<int, SharedAdminLog[]>
        {
            [selectedRound] = expected,
            [currentRound] = [new SharedAdminLog(1, LogType.Unknown, LogImpact.Low, date, "другой раунд", [])]
        };
        var snapshot = new AdminLogExportSnapshot(selectedRound, expected.Length, expected[^1].Id, date);
        var pageRequests = new List<(int Offset, int AfterId, int Limit)>();
        var logs = new Mock<IAdminLogManager>(MockBehavior.Strict);
        logs.Setup(manager => manager.CreateExportSnapshotAsync(selectedRound, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        logs.Setup(manager => manager.ReadExportPageAsync(snapshot, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((AdminLogExportSnapshot captured, int offset, int afterId, int limit, CancellationToken cancel) =>
            {
                cancel.ThrowIfCancellationRequested();
                pageRequests.Add((offset, afterId, limit));
                IReadOnlyList<SharedAdminLog> page = rounds[captured.RoundId].Skip(offset)
                    .Take(Math.Min(limit, captured.Count - offset)).ToArray();
                return Task.FromResult(page);
            });

        var queue = new Queue<(bool ToClient, EuiMessageBase Message)>();
        var serverMessages = new List<EuiMessageBase>();
        var clientMessages = new List<EuiMessageBase>();
        var audit = new List<string>();
        var errors = new List<Exception>();
        var exports = new AdminLogExportSystem();
        var user = new NetUserId(firstPlayer);
        var server = new AdminLogExportSession(user, logs.Object, exports, () => TimeSpan.Zero,
            () => true, () => currentRound, message =>
            {
                serverMessages.Add(message);
                queue.Enqueue((true, message));
            }, audit.Add);
        using var temp = new MemoryStream();
        using var saved = new MemoryStream();
        var filesCreated = 0;
        var filesDeleted = 0;
        var filesPublished = 0;
        var client = new AdminLogExportClient(() =>
        {
            filesCreated++;
            return new AdminLogExportFile(temp, () => filesDeleted++, _ =>
            {
                filesPublished++;
                saved.Write(temp.ToArray());
            });
        }, message =>
        {
            clientMessages.Add(message);
            queue.Enqueue((false, message));
        }, errors.Add);

        // Transport callbacks enqueue replies; neither endpoint re-enters itself before its await completes.
        async Task PumpAsync()
        {
            var delivered = 0;
            while (queue.TryDequeue(out var next))
            {
                Assert.That(++delivered, Is.LessThan(1000), "The transfer must finish with bounded messages.");
                if (next.ToClient)
                    await client.HandleAsync(next.Message);
                else
                    await server.HandleMessageAsync(next.Message);
            }
        }

        client.SetPermission(true);
        client.Begin(selectedRound);
        await PumpAsync();
        for (var stage = 1; stage <= 3; stage++)
        {
            Assert.That(client.CanConfirm, Is.True);
            Assert.That(client.ConfirmationStage, Is.EqualTo(stage));
            Assert.That(logs.Invocations, Is.Empty, "No data is read before the third confirmation.");
            Assert.That(filesCreated, Is.Zero);
            client.Confirm();
            await PumpAsync();
        }

        var chunks = serverMessages.OfType<AdminLogExportChunk>().ToArray();
        var completed = serverMessages.OfType<AdminLogExportCompleted>().Single();
        var bytes = saved.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(serverMessages.OfType<AdminLogExportError>(), Is.Empty);
            Assert.That(serverMessages.OfType<AdminLogExportPrompt>().Select(prompt => prompt.Stage),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(clientMessages.OfType<AdminLogExportConfirm>().Count(), Is.EqualTo(3));
            Assert.That(serverMessages.OfType<AdminLogExportStarted>().Single().RoundId, Is.EqualTo(selectedRound));
            Assert.That(pageRequests, Is.EqualTo(new[]
            {
                (0, 0, AdminLogExportLimits.PageLogs),
                (128, expected[127].Id, AdminLogExportLimits.PageLogs),
                (256, expected[255].Id, AdminLogExportLimits.PageLogs)
            }));
            Assert.That(chunks.Length, Is.GreaterThan(1));
            Assert.That(chunks.Select(chunk => chunk.Sequence), Is.EqualTo(Enumerable.Range(0, chunks.Length)));
            Assert.That(chunks.All(chunk => chunk.Data.Length <= AdminLogExportLimits.ChunkBytes), Is.True);
            Assert.That(bytes.Length, Is.GreaterThan(AdminLogExportLimits.ChunkBytes));
            Assert.That(bytes, Is.EqualTo(chunks.SelectMany(chunk => chunk.Data).ToArray()));
            Assert.That(bytes, Is.EqualTo(temp.ToArray()));
            Assert.That(completed.Bytes, Is.EqualTo(bytes.Length));
            Assert.That(completed.Logs, Is.EqualTo(expected.Length));
            Assert.That(completed.Chunks, Is.EqualTo(chunks.Length));
            Assert.That(clientMessages.OfType<AdminLogExportLocalResult>().Single().Saved, Is.True);
            Assert.That(filesCreated, Is.EqualTo(1));
            Assert.That(filesDeleted, Is.EqualTo(1));
            Assert.That(filesPublished, Is.EqualTo(1));
            Assert.That(client.Busy, Is.False);
            Assert.That(client.StatusKey, Is.EqualTo("admin-logs-export-saved"));
            Assert.That(exports.ActiveCount, Is.Zero);
            Assert.That(audit.Count, Is.EqualTo(3));
            Assert.That(audit[0], Does.Contain("requested"));
            Assert.That(audit[1], Does.Contain("transfer completed"));
            Assert.That(audit[2], Does.Contain("client-reported saved").And.Contain(user.ToString())
                .And.Contain($"round={selectedRound}").And.Contain($"logs={expected.Length}")
                .And.Contain($"bytes={bytes.Length}"));
        });

        var lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n');
        Assert.That(lines.Length, Is.EqualTo(expected.Length + 3));
        Assert.That(lines[^1], Is.Empty, "The final JSONL record is newline terminated.");
        using var manifest = JsonDocument.Parse(lines[0]);
        Assert.Multiple(() =>
        {
            Assert.That(manifest.RootElement.GetProperty("kind").GetString(), Is.EqualTo("round_metadata"));
            Assert.That(manifest.RootElement.GetProperty("roundId").GetInt32(), Is.EqualTo(selectedRound));
            Assert.That(manifest.RootElement.GetProperty("snapshotUtc").GetDateTime(), Is.EqualTo(date));
            Assert.That(manifest.RootElement.GetProperty("totalLogs").GetInt32(), Is.EqualTo(expected.Length));
            Assert.That(manifest.RootElement.GetProperty("lastLogId").GetInt32(), Is.EqualTo(expected[^1].Id));
        });
        for (var index = 0; index < expected.Length; index++)
        {
            using var document = JsonDocument.Parse(lines[index + 1]);
            var actual = document.RootElement;
            var original = expected[index];
            Assert.Multiple(() =>
            {
                Assert.That(actual.GetProperty("kind").GetString(), Is.EqualTo("log"));
                Assert.That(actual.GetProperty("id").GetInt32(), Is.EqualTo(original.Id));
                Assert.That(actual.GetProperty("type").GetInt32(), Is.EqualTo((int) original.Type));
                Assert.That(actual.GetProperty("impact").GetInt32(), Is.EqualTo((int) original.Impact));
                Assert.That(actual.GetProperty("date").GetDateTime(), Is.EqualTo(original.Date));
                Assert.That(actual.GetProperty("message").GetString(), Is.EqualTo(original.Message));
                Assert.That(actual.GetProperty("players").EnumerateArray().Select(player => player.GetGuid()),
                    Is.EqualTo(original.Players));
            });
        }

        using var footer = JsonDocument.Parse(lines[^2]);
        Assert.That(footer.RootElement.GetProperty("kind").GetString(), Is.EqualTo("complete"));
        Assert.That(footer.RootElement.GetProperty("logs").GetInt32(), Is.EqualTo(expected.Length));
        logs.Verify(manager => manager.CreateExportSnapshotAsync(selectedRound, It.IsAny<CancellationToken>()), Times.Once);
        logs.Verify(manager => manager.ReadExportPageAsync(snapshot, It.IsAny<int>(), It.IsAny<int>(),
            AdminLogExportLimits.PageLogs, It.IsAny<CancellationToken>()), Times.Exactly(3));
        logs.VerifyNoOtherCalls();
        await client.CloseAsync();
        server.Close();
    }
}
