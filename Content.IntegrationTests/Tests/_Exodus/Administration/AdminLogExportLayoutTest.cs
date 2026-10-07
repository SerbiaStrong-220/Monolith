using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Exodus.Administration.LogExport;
using Content.Client.Administration.UI.Logs;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Eui;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus.Administration;

[TestFixture]
public sealed class AdminLogExportLayoutTest
{
    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task SavedExportShowsItsFileAndFolderActionWhileInitialStatusStaysHidden(string culture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var loc = pair.Client.ResolveDependency<ILocalizationManager>();
        CultureInfo original = null!;
        AdminLogsWindow window = null!;
        AdminLogExportClient export = null!;
        Button folder = null!;
        var storage = new VirtualWritableDirProvider();
        var opened = 0;
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                original = loc.DefaultCulture;
                loc.SetCulture(CultureInfo.GetCultureInfo(culture));
                window = new AdminLogsWindow();
                export = new AdminLogExportClient(() => AdminLogExportFile.Create(storage), _ => { },
                    openFolder: () => opened++);
                var panel = window.Logs.ExportPanel;
                panel.Bind(export, () => 17);
                export.SetPermission(true);
                Assert.That(Descendants(panel).OfType<Label>().Single(label =>
                    label.Text == Loc.GetString("admin-logs-export-ready")).Visible, Is.False);
                folder = Descendants(panel).OfType<Button>().Single(button =>
                    button.Text == Loc.GetString("admin-logs-export-open-folder"));
                Assert.That(folder.Visible, Is.False);
                export.Begin(17);
            });
            var id = Guid.NewGuid();
            for (var stage = 1; stage <= 3; stage++)
            {
                await Deliver(new AdminLogExportPrompt(id, 17, stage, "token" + stage));
                await pair.Client.WaitPost(export.Confirm);
            }
            await Deliver(new AdminLogExportStarted(id, 17, DateTime.UtcNow, 1));
            byte[] bytes = [123, 125, 10, 123, 125, 10, 123, 125, 10];
            await Deliver(new AdminLogExportChunk(id, 0, bytes));
            await Deliver(new AdminLogExportCompleted(id, bytes.Length, 1, 1));
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(export.SavedPath, Is.Not.Null);
                var savedPath = export.SavedPath!.Value;
                Assert.That(storage.ReadAllBytes(savedPath), Is.EqualTo(bytes));
                Assert.That(folder.Visible, Is.True);
                Assert.That(Descendants(window.Logs.ExportPanel).OfType<Label>().Any(label => label.Visible &&
                    label.Text?.Contains(savedPath.Filename, StringComparison.Ordinal) == true), Is.True);
                export.OpenFolder();
                Assert.That(opened, Is.EqualTo(1));
            });
        }
        finally
        {
            var closing = Task.CompletedTask;
            await pair.Client.WaitPost(() =>
            {
                window?.Logs.ExportPanel.Unbind();
                if (export != null)
                    closing = export.CloseAsync();
            });
            try
            {
                await PoolManager.WaitUntil(pair.Client, () => closing.IsCompleted);
                await closing;
            }
            finally
            {
                await pair.Client.WaitPost(() =>
                {
                    window?.Dispose();
                    if (original != null)
                        loc.DefaultCulture = original;
                });
            }
        }
        await pair.CleanReturnAsync();

        async Task Deliver(EuiMessageBase message)
        {
            var handling = Task.CompletedTask;
            await pair.Client.WaitPost(() =>
            {
                handling = export.HandleAsync(message);
            });
            await PoolManager.WaitUntil(pair.Client, () => handling.IsCompleted);
            await handling;
        }
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task FinalExportConfirmationFitsTheActualLogWindow(string culture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            try
            {
                using var window = new AdminLogsWindow();
                var export = new AdminLogExportClient(
                    () => throw new AssertionException("No file should be created during confirmation."),
                    _ => { });
                window.Logs.ExportPanel.Bind(export, () => 17);
                export.SetPermission(true);
                export.Begin(17);
                var id = Guid.NewGuid();
                for (var stage = 1; stage <= 3; stage++)
                {
                    Assert.That(export.HandleAsync(new AdminLogExportPrompt(id, 17, stage, "token" + stage)).IsCompleted, Is.True);
                    if (stage < 3)
                        export.Confirm();
                }

                window.OpenCentered();
                var size = new Vector2(1100, 400);
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var panel = window.Logs.ExportPanel;
                Assert.That(panel.IsInsideTree, Is.True);
                Assert.That(panel.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var font), Is.True);
                Assert.That(font.GetLineHeight(panel.UIScale), Is.GreaterThan(0));
                foreach (var control in Descendants(panel).Where(control => control.VisibleInTree && control is Button or RichTextLabel or Label))
                {
                    Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(window.GlobalRect.Right + 0.1f));
                    Assert.That(control.GlobalRect.Bottom, Is.LessThanOrEqualTo(window.GlobalRect.Bottom + 0.1f));
                }

                Assert.That(Descendants(panel).OfType<RichTextLabel>().Any(label =>
                    label.VisibleInTree && label.GetMessage()?.Contains("17", StringComparison.Ordinal) == true), Is.True);
                panel.Unbind();
                Assert.That(export.CloseAsync().IsCompleted, Is.True);
                window.Close();
            }
            finally
            {
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
