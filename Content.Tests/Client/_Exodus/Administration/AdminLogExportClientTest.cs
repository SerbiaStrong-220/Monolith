#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._Exodus.Administration.LogExport;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Eui;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Tests.Client._Exodus.Administration;

[TestFixture]
public sealed class AdminLogExportClientTest
{
    private static readonly byte[] OneLog = Encoding.UTF8.GetBytes("{\"kind\":\"manifest\"}\n{}\n{\"kind\":\"complete\"}\n");

    [Test]
    public async Task UnsolicitedMessagesNeverCreateOrPublishFiles()
    {
        var harness = new Harness();
        var id = Guid.NewGuid();
        await harness.Client.HandleAsync(new AdminLogExportPrompt(id, 7, 1, "token"));
        await harness.Client.HandleAsync(new AdminLogExportStarted(id, 7, DateTime.UtcNow, 1));
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, Encoding.UTF8.GetBytes("{}\n")));
        await harness.Client.HandleAsync(new AdminLogExportCompleted(id, 3, 1, 1));
        Assert.That(harness.FilesCreated, Is.Zero);
        Assert.That(harness.FilesPublished, Is.Zero);
        Assert.That(harness.Messages, Is.Empty);
    }

    [Test]
    public async Task ThreeOwnConfirmationsAreRequiredBeforeReceivingData()
    {
        var harness = new Harness();
        harness.Client.SetPermission(true);
        harness.Client.Begin(7);
        var id = Guid.NewGuid();
        await harness.Client.HandleAsync(new AdminLogExportPrompt(id, 7, 1, "first"));
        harness.Client.Confirm();
        await harness.Client.HandleAsync(new AdminLogExportStarted(id, 7, DateTime.UtcNow, 1));
        Assert.That(harness.FilesCreated, Is.Zero);
        Assert.That(harness.Messages.OfType<AdminLogExportNext>(), Is.Empty);
    }

    [Test]
    public async Task CancelBeforePromptDrainsTheOldChallengeBeforeAllowingANewBegin()
    {
        var harness = new Harness();
        harness.Client.SetPermission(true);
        harness.Client.Begin(7);
        await harness.Client.CancelAsync();
        harness.Client.Begin(7);
        Assert.That(harness.Messages.OfType<AdminLogExportBegin>().Count(), Is.EqualTo(1));
        var oldId = Guid.NewGuid();
        await harness.Client.HandleAsync(new AdminLogExportPrompt(oldId, 7, 1, "stale"));
        Assert.That(harness.Messages.OfType<AdminLogExportCancel>().Single().Id, Is.EqualTo(oldId));
        Assert.That(harness.Client.CanConfirm, Is.False);
        harness.Client.Begin(7);
        Assert.That(harness.Messages.OfType<AdminLogExportBegin>().Count(), Is.EqualTo(2));
        Assert.That(harness.FilesCreated, Is.Zero);
    }

    [Test]
    public async Task Utf8ChunksArePublishedExactlyAndTempIsDeleted()
    {
        var harness = new Harness();
        var id = await Authorize(harness, 2);
        var bytes = Encoding.UTF8.GetBytes("{\"kind\":\"manifest\"}\n{\"Message\":\"Кислород\"}\n{\"Message\":\"Привет\"}\n{\"kind\":\"complete\"}\n");
        var split = Array.IndexOf(bytes, (byte) 0xD0) + 1; // Deliberately splits a multibyte UTF-8 character.
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, bytes[..split]));
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 1, bytes[split..]));
        Assert.That(harness.Messages.OfType<AdminLogExportNext>().Select(next => next.Sequence), Is.EqualTo(new[] { 0, 1, 2 }));
        await harness.Client.HandleAsync(new AdminLogExportCompleted(id, bytes.Length, 2, 2));
        Assert.That(harness.Saved.ToArray(), Is.EqualTo(bytes));
        Assert.That(harness.Deleted, Is.True);
        Assert.That(harness.Client.Busy, Is.False);
        Assert.That(harness.Messages.OfType<AdminLogExportLocalResult>().Single().Saved, Is.True);
        Assert.That(harness.Client.SavedPath?.Filename, Does.StartWith("round-7-").And.EndWith(".jsonl"));
        Assert.That(harness.Client.CanOpenFolder, Is.True);
        harness.Client.OpenFolder();
        Assert.That(harness.FoldersOpened, Is.EqualTo(1));
    }

    [Test]
    public async Task ExistingDestinationIsNeverTruncatedOrWritten()
    {
        var harness = new Harness { ExistingDestination = true };
        var original = Encoding.UTF8.GetBytes("do not overwrite");
        harness.Saved.Write(original);
        var id = await Authorize(harness, 1);
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, OneLog));
        await harness.Client.HandleAsync(new AdminLogExportCompleted(id, OneLog.Length, 1, 1));
        Assert.That(harness.Saved.ToArray(), Is.EqualTo(original));
        Assert.That(harness.Messages.OfType<AdminLogExportLocalResult>().Any(result => result.Saved), Is.False);
        Assert.That(harness.Client.StatusKey, Is.EqualTo("admin-logs-export-error-local-io"));
        Assert.That(harness.Client.CanOpenFolder, Is.False);
        Assert.That(harness.Deleted, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task InvalidSequenceOrSizeNeverPublishesAFile(bool invalidSequence)
    {
        var harness = new Harness();
        var id = await Authorize(harness, 1);
        await harness.Client.HandleAsync(new AdminLogExportChunk(id,
            invalidSequence ? 3 : 0,
            new byte[invalidSequence ? 1 : AdminLogExportLimits.ChunkBytes + 1]));
        Assert.That(harness.FilesPublished, Is.Zero);
        Assert.That(harness.Deleted, Is.True);
        Assert.That(harness.Messages.OfType<AdminLogExportNext>().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task IncompleteRecordCountNeverProducesSuccess()
    {
        var harness = new Harness();
        var id = await Authorize(harness, 2);
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, Encoding.UTF8.GetBytes("{}\n")));
        await harness.Client.HandleAsync(new AdminLogExportCompleted(id, 3, 2, 1));
        Assert.That(harness.FilesPublished, Is.Zero);
        Assert.That(harness.Messages.OfType<AdminLogExportLocalResult>().Any(result => result.Saved), Is.False);
        Assert.That(harness.Deleted, Is.True);
    }

    [Test]
    public async Task CancellationWaitsForOutstandingWriteAndDoesNotAcknowledgeIt()
    {
        var slow = new DelayedStream();
        var harness = new Harness(slow);
        var id = await Authorize(harness, 1);
        var writing = harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, Encoding.UTF8.GetBytes("{}\n")));
        await slow.Started.Task;
        var cancel = harness.Client.CancelAsync();
        Assert.That(slow.WasDisposed, Is.False, "Cleanup must not dispose a stream while WriteAsync still uses it.");
        Assert.That(harness.Deleted, Is.False);
        slow.Release.SetResult();
        await Task.WhenAll(writing, cancel);
        Assert.That(slow.WasDisposed, Is.True);
        Assert.That(harness.Deleted, Is.True);
        Assert.That(harness.Messages.OfType<AdminLogExportNext>().Count(), Is.EqualTo(1));
        Assert.That(harness.FilesPublished, Is.Zero);
    }

    [Test]
    public async Task RepeatedCancelAndCloseShareCleanupWhileDiskWriteIsOutstanding()
    {
        var slow = new DelayedStream();
        var harness = new Harness(slow);
        var id = await Authorize(harness, 1);
        var writing = harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, OneLog));
        await slow.Started.Task;
        var firstCancel = harness.Client.CancelAsync();
        var secondCancel = harness.Client.CancelAsync();
        harness.Client.SetPermission(false);
        var closing = harness.Client.CloseAsync();
        Assert.That(slow.WasDisposed, Is.False);
        Assert.That(harness.DeleteCount, Is.Zero);
        Assert.That(harness.Messages.OfType<AdminLogExportCancel>().Count(), Is.EqualTo(1));
        slow.Release.SetResult();
        await Task.WhenAll(writing, firstCancel, secondCancel, closing);
        await harness.Client.CancelAsync();
        await harness.Client.CloseAsync();
        Assert.That(harness.DeleteCount, Is.EqualTo(1));
        Assert.That(slow.DisposeCount, Is.EqualTo(1));
        Assert.That(harness.Client.Busy, Is.False);
        Assert.That(harness.Messages.OfType<AdminLogExportNext>().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task ClosingWhileFileIsClosingNeverPublishesOrReportsSuccess()
    {
        var stream = new DelayedCloseStream();
        var harness = new Harness(stream);
        var id = await Authorize(harness, 1);
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, OneLog));
        var completion = harness.Client.HandleAsync(new AdminLogExportCompleted(id, OneLog.Length, 1, 1));
        await stream.Started.Task;
        Assert.That(harness.FilesPublished, Is.Zero);
        var closing = harness.Client.CloseAsync();
        var count = harness.Messages.Count;
        stream.Release.SetResult();
        await Task.WhenAll(completion, closing);
        Assert.That(harness.Saved.ToArray(), Is.Empty);
        Assert.That(harness.Messages.Count, Is.EqualTo(count));
        Assert.That(harness.Deleted, Is.True);
        Assert.That(stream.DisposeCount, Is.EqualTo(1));
        Assert.That(harness.FilesPublished, Is.Zero);
    }

    [Test]
    public async Task DiskFailureCleansTempAndNeverAcknowledgesTheFailedChunk()
    {
        var harness = new Harness(new FailingStream());
        var id = await Authorize(harness, 1);
        await harness.Client.HandleAsync(new AdminLogExportChunk(id, 0, OneLog));
        Assert.That(harness.Deleted, Is.True);
        Assert.That(harness.FilesPublished, Is.Zero);
        Assert.That(harness.Messages.OfType<AdminLogExportNext>().Count(), Is.EqualTo(1));
        Assert.That(harness.Client.StatusKey, Is.EqualTo("admin-logs-export-error-local-io"));
    }

    [Test]
    public async Task UserDataCleanupOnlyDeletesItsOwnRandomTemporaryFile()
    {
        var storage = new VirtualWritableDirProvider();
        var directory = new ResPath("/admin-log-exports");
        storage.CreateDir(directory);
        var existing = directory / "keep.txt";
        using (storage.Open(existing, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
        }

        var file = AdminLogExportFile.Create(storage);
        Assert.That(storage.DirectoryEntries(directory).Count(), Is.EqualTo(2));
        await file.WriteAsync(0, OneLog, CancellationToken.None);
        await file.CompleteAsync(new AdminLogExportCompleted(Guid.NewGuid(), OneLog.Length, 1, 1), 1, CancellationToken.None);
        await file.DisposeAsync();
        Assert.That(storage.Exists(existing), Is.True);
        Assert.That(storage.DirectoryEntries(directory).Count(), Is.EqualTo(1));
    }

    private static async Task<Guid> Authorize(Harness harness, int logs)
    {
        harness.Client.SetPermission(true);
        harness.Client.Begin(7);
        var id = Guid.NewGuid();
        for (var stage = 1; stage <= 3; stage++)
        {
            await harness.Client.HandleAsync(new AdminLogExportPrompt(id, 7, stage, "token-" + stage));
            Assert.That(harness.Client.CanConfirm, Is.True);
            harness.Client.Confirm();
        }

        await harness.Client.HandleAsync(new AdminLogExportStarted(id, 7, DateTime.UtcNow, logs));
        Assert.That(harness.FilesCreated, Is.EqualTo(1));
        return id;
    }

    private sealed class Harness
    {
        public readonly AdminLogExportClient Client;
        public readonly List<EuiMessageBase> Messages = new();
        public readonly MemoryStream Saved = new();
        public int FilesCreated;
        public int FilesPublished;
        public int FoldersOpened;
        public bool Deleted;
        public int DeleteCount;
        public bool ExistingDestination;

        public Harness(MemoryStream? temp = null)
        {
            var stream = temp ?? new MemoryStream();
            Client = new AdminLogExportClient(
                () =>
                {
                    FilesCreated++;
                    return new AdminLogExportFile(stream, () =>
                    {
                        Deleted = true;
                        DeleteCount++;
                    }, _ =>
                    {
                        FilesPublished++;
                        if (ExistingDestination)
                            throw new IOException("Destination already exists.");
                        Saved.Write(stream.ToArray());
                    });
                },
                Messages.Add,
                openFolder: () => FoldersOpened++);
        }
    }

    private sealed class DelayedStream : MemoryStream
    {
        public readonly TaskCompletionSource Started = new();
        public readonly TaskCompletionSource Release = new();
        public bool WasDisposed;
        public int DisposeCount;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task;
            await base.WriteAsync(buffer, CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new IOException("Test disk failure."));
        }
    }

    private sealed class DelayedCloseStream : MemoryStream
    {
        public readonly TaskCompletionSource Started = new();
        public readonly TaskCompletionSource Release = new();
        public int DisposeCount;

        public override async ValueTask DisposeAsync()
        {
            Started.SetResult();
            await Release.Task;
            DisposeCount++;
            await base.DisposeAsync();
        }
    }
}
