using System;
using System.Collections.Generic;
using System.IO;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._Exodus.Administration;

[TestFixture]
public sealed class AdminLogExportProtocolTest
{
    [Test]
    public async Task ExportProtocolRetainsRoundAuthorizationAndChunkDataAcrossTheNetworkSerializer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var id = Guid.NewGuid();
        var snapshotTime = new DateTime(2026, 10, 6, 12, 34, 56, DateTimeKind.Utc);
        EuiMessageBase[] messages =
        [
            new AdminLogExportBegin(37),
            new AdminLogExportPrompt(id, 37, 2, "single-use-token"),
            new AdminLogExportConfirm(id, 2, "single-use-token"),
            new AdminLogExportStarted(id, 37, snapshotTime, 1024),
            new AdminLogExportNext(id, 23),
            new AdminLogExportChunk(id, 23, [0xD0, 0xBA, 0xD0, 0xBE, 0x0A]),
            new AdminLogExportCompleted(id, 745632, 1024, 24),
            new AdminLogExportCancel(id),
            new AdminLogExportLocalResult(id, true),
            new AdminLogExportError(id, "admin-logs-export-error-permission"),
        ];
        var bytes = new List<byte[]>();
        await pair.Server.WaitAssertion(() =>
        {
            var serializer = pair.Server.ResolveDependency<IRobustSerializer>();
            foreach (var message in messages)
            {
                using var stream = new MemoryStream();
                serializer.Serialize(stream, message);
                bytes.Add(stream.ToArray());
            }

            using var stateStream = new MemoryStream();
            serializer.Serialize(stateStream, new AdminLogsEuiState(37, new(), 1024)
            {
                CanExportRoundLogs = true,
            });
            bytes.Add(stateStream.ToArray());
        });
        await pair.Client.WaitAssertion(() =>
        {
            var serializer = pair.Client.ResolveDependency<IRobustSerializer>();
            for (var index = 0; index < messages.Length; index++)
            {
                using var stream = new MemoryStream(bytes[index]);
                var decoded = serializer.Deserialize<EuiMessageBase>(stream);
                var expected = messages[index];
                Assert.That(decoded.GetType(), Is.EqualTo(expected.GetType()));
                foreach (var property in expected.GetType().GetProperties())
                    Assert.That(property.GetValue(decoded), Is.EqualTo(property.GetValue(expected)), property.Name);
            }

            using var stateStream = new MemoryStream(bytes[^1]);
            var state = serializer.Deserialize<AdminLogsEuiState>(stateStream);
            Assert.That(state.CanExportRoundLogs, Is.True);
            Assert.That(state.RoundId, Is.EqualTo(37));
        });
        await pair.CleanReturnAsync();
    }
}
