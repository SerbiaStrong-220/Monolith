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
public sealed class AdminLogExportFileTest
{
    private static readonly byte[] Records = Encoding.UTF8.GetBytes("{}\n{\"message\":\"Кислород\\nстрока\"}\n{}\n");
    private string _directory = default!;
    private IWritableDirProvider _storage = default!;
    private static readonly ResPath ExportDirectory = new("/admin-log-exports");
    private static readonly ResPath Destination = ExportDirectory / "round-7-completed.jsonl";

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "log-export-files-" + Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(_directory);
        // Use the engine's actual disk provider, including its non-overwriting File.Move implementation.
        var providerType = typeof(IWritableDirProvider).Assembly.GetType("Robust.Shared.ContentPack.WritableDirProvider", true)!;
        _storage = (IWritableDirProvider) Activator.CreateInstance(providerType, directory, true)!;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public async Task OnlyValidatedCompleteFileCanBePublishedAndCleanupKeepsTheSavedFile()
    {
        await using (var file = AdminLogExportFile.Create(_storage))
        {
            await file.WriteAsync(0, Records, CancellationToken.None);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await file.SaveAsync(Destination, CancellationToken.None));
            Assert.That(_storage.Exists(Destination), Is.False);
            await Seal(file);
            await file.SaveAsync(Destination, CancellationToken.None);
            Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.EqualTo(new[] { Destination.Filename }));
        }

        Assert.That(_storage.ReadAllBytes(Destination), Is.EqualTo(Records));
        Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.EqualTo(new[] { Destination.Filename }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingFileOrDirectoryBlocksRenameWithoutOverwritingAnything(bool directoryCollision)
    {
        var original = Encoding.UTF8.GetBytes("existing destination must survive");
        await using (var file = AdminLogExportFile.Create(_storage))
        {
            await file.WriteAsync(0, Records, CancellationToken.None);
            await Seal(file);
            if (directoryCollision)
                _storage.CreateDir(Destination);
            else
                _storage.WriteAllBytes(Destination, original);

            Assert.ThrowsAsync<IOException>(async () => await file.SaveAsync(Destination, CancellationToken.None));
            Assert.That(_storage.DirectoryEntries(ExportDirectory).Count(), Is.EqualTo(2));
        }

        Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.EqualTo(new[] { Destination.Filename }));
        if (directoryCollision)
            Assert.That(_storage.IsDir(Destination), Is.True);
        else
            Assert.That(_storage.ReadAllBytes(Destination), Is.EqualTo(original));
    }

    [Test]
    public async Task CancellationBeforePublishRemovesTheTemporaryFileWithoutCreatingAJsonl()
    {
        await using (var file = AdminLogExportFile.Create(_storage))
        {
            await file.WriteAsync(0, Records, CancellationToken.None);
            await Seal(file);
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await file.SaveAsync(Destination, new CancellationToken(true)));
        }

        Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.Empty);
    }

    [Test]
    public async Task DisposingPartialDownloadLeavesNoCompletedOrTemporaryFile()
    {
        await using (var file = AdminLogExportFile.Create(_storage))
            await file.WriteAsync(0, Records[..8], CancellationToken.None);
        Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellingOrDisconnectingAPartialTransferCleansTheRealDisk(bool disconnect)
    {
        var messages = new List<EuiMessageBase>();
        var client = new AdminLogExportClient(() => AdminLogExportFile.Create(_storage), messages.Add);
        client.SetPermission(true);
        client.Begin(7);
        var id = Guid.NewGuid();
        for (var stage = 1; stage <= 3; stage++)
        {
            await client.HandleAsync(new AdminLogExportPrompt(id, 7, stage, "token-" + stage));
            client.Confirm();
        }
        await client.HandleAsync(new AdminLogExportStarted(id, 7, DateTime.UtcNow, 1));
        await client.HandleAsync(new AdminLogExportChunk(id, 0, Records[..8]));
        Assert.That(_storage.DirectoryEntries(ExportDirectory).Count(), Is.EqualTo(1));
        if (disconnect)
            await client.CloseAsync();
        else
            await client.CancelAsync();

        Assert.That(_storage.DirectoryEntries(ExportDirectory), Is.Empty);
        Assert.That(client.SavedPath, Is.Null);
        Assert.That(messages.OfType<AdminLogExportLocalResult>().Any(result => result.Saved), Is.False);
    }

    private static Task Seal(AdminLogExportFile file)
    {
        return file.CompleteAsync(new AdminLogExportCompleted(Guid.NewGuid(), Records.Length, 1, 1), 1, CancellationToken.None);
    }
}
