// Exodus: шуточные изменения — тесты файловой конфигурации титулов; удаляются вместе с функцией при откате.
using System;
using System.IO;
using System.Text;
using Content.Server._Exodus.Administration;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Managers;
using Content.Server.Afk;
using Content.Shared.Administration;
using Moq;
using NUnit.Framework;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Server._Exodus.Administration;

[TestFixture]
public sealed class FixedAdminTitlesTest
{
    private const string FixedTitle = "Симплмоб младший";
    private const string Configuration = """
        # Exodus: шуточные изменения — тестовый YAML-конфиг.
        enabled: true
        titles:
          OperatorXeno: "Симплмоб младший" # Inline comments are supported too.
          AnotherAdmin: Повелитель кнопок
        """;

    private string _directory;
    private string _path;
    private FixedAdminTitles _titles;
    private Mock<ISawmill> _log;

    [SetUp]
    public void SetUp()
    {
        IoCManager.InitThread();
        IoCManager.Clear();
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "admin-title-pranks", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, FixedAdminTitles.FileName);
        _log = new Mock<ISawmill>();
        _titles = Load(Configuration);
    }

    [TearDown]
    public void TearDown()
    {
        IoCManager.Clear();
        Directory.Delete(_directory, recursive: true);
    }

    [TestCase("OperatorXeno", "Chief of Admins")]
    [TestCase("operatorxeno", "Custom title")]
    [TestCase("OPERATORXENO", "Host")]
    [TestCase("OperatorXeno", "")]
    [TestCase("OperatorXeno", null)]
    public void AccountTitleOverridesRankAndCustomTitle(string accountName, string title)
    {
        var session = MakeSession(accountName, "Renamed session");
        var admin = new AdminData { Active = true, Title = title, ShortTitle = "CA", Flags = AdminFlags.Admin };

        Assert.That(_titles.GetTitle(session, admin), Is.EqualTo(FixedTitle));
        Assert.Multiple(() =>
        {
            Assert.That(admin.Title, Is.EqualTo(title));
            Assert.That(admin.ShortTitle, Is.EqualTo("CA"));
            Assert.That(admin.Flags, Is.EqualTo(AdminFlags.Admin));
            Assert.That(admin.Active, Is.True);
        });
    }

    [TestCase("OtherAdmin", "Chief of Admins")]
    [TestCase("OperatorXeno2", "Custom title")]
    [TestCase("guest@OperatorXeno", "Host")]
    [TestCase("OtherAdmin", null)]
    public void DisplayNameCannotImpersonateAccount(string accountName, string title)
    {
        var session = MakeSession(accountName, "OperatorXeno");
        Assert.That(_titles.GetTitle(session, new AdminData { Title = title }), Is.EqualTo(title));
    }

    [Test]
    public void NonAdminDoesNotGetTitle()
    {
        Assert.That(_titles.GetTitle(MakeSession("OperatorXeno"), null), Is.Null);
    }

    [Test]
    public void SessionWithoutChannelKeepsOriginalTitle()
    {
        var session = new Mock<ICommonSession>();
        session.SetupGet(s => s.Name).Returns("OperatorXeno");
        Assert.That(_titles.GetTitle(session.Object, new AdminData { Title = "Host" }), Is.EqualTo("Host"));
    }

    [Test]
    public void AdminWhoIgnoresTitleEditsAndReplacedAdminData()
    {
        var session = MakeSession("OperatorXeno", "Renamed session");
        var admin = new AdminData { Active = true, Title = "Chief of Admins" };
        var admins = RegisterAdmins(session, () => admin);
        var shell = new Mock<IConsoleShell>();
        shell.SetupGet(s => s.Player).Returns(MakeSession("Player"));
        var command = new AdminWhoCommand();

        command.Execute(shell.Object, "", []);
        admin.Title = "Changed through VV";
        command.Execute(shell.Object, "", []);
        admin = new AdminData { Active = true, Title = "New rank after reload" };
        command.Execute(shell.Object, "", []);

        shell.Verify(s => s.WriteLine($"Renamed session: [{FixedTitle}]"), Times.Exactly(3));
        admins.Verify(a => a.GetAdminData(session, false), Times.Exactly(3));
    }

    [TestCase(false, true, false, "")]
    [TestCase(true, false, false, "OperatorXeno: [Симплмоб младший]")]
    [TestCase(true, true, false, "")]
    [TestCase(true, true, true, "OperatorXeno: [Симплмоб младший] (S)")]
    public void AdminWhoPreservesActiveAndStealthFiltering(bool active, bool stealth, bool serverConsole, string expected)
    {
        var session = MakeSession("OperatorXeno");
        var admin = new AdminData { Active = active, Stealth = stealth, Title = "Chief of Admins" };
        RegisterAdmins(session, () => admin);
        var shell = new Mock<IConsoleShell>();
        if (!serverConsole)
            shell.SetupGet(s => s.Player).Returns(MakeSession("Player"));

        new AdminWhoCommand().Execute(shell.Object, "", []);

        shell.Verify(s => s.WriteLine(expected), Times.Once);
    }

    [Test]
    public void MultipleAccountsHaveIndependentTitles()
    {
        var admin = new AdminData { Title = "Original" };
        Assert.That(_titles.GetTitle(MakeSession("anotheradmin"), admin), Is.EqualTo("Повелитель кнопок"));
        Assert.That(_titles.GetTitle(MakeSession("OperatorXeno"), admin), Is.EqualTo(FixedTitle));
        Assert.That(_titles.GetTitle(MakeSession("UnlistedAdmin"), admin), Is.EqualTo("Original"));
    }

    [Test]
    public void Utf8BomIsSupported()
    {
        File.WriteAllText(_path, Configuration, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var titles = FixedAdminTitles.LoadFromFile(_path, _log.Object);
        Assert.That(titles.GetTitle(MakeSession("OperatorXeno"), new AdminData()), Is.EqualTo(FixedTitle));
    }

    [TestCase("Leading space ")]
    [TestCase("Tab\tseparator")]
    [TestCase("Line\u2028separator")]
    [TestCase("Paragraph\u2029separator")]
    public void InvalidTitleTextDisablesFeature(string title)
    {
        Assert.That(LoadTitle(title), Is.SameAs(FixedAdminTitles.Disabled));
    }

    [Test]
    public void OversizedTitleDisablesFeature()
    {
        Assert.That(LoadTitle(new string('x', 201)), Is.SameAs(FixedAdminTitles.Disabled));
    }

    [TestCase("Админ: #1 — \"главный\"")]
    [TestCase("null")]
    [TestCase("~")]
    [TestCase("42")]
    public void QuotedTitlesPreserveLiteralText(string title)
    {
        Assert.That(LoadTitle(title).GetTitle(MakeSession("OperatorXeno"), new AdminData()), Is.EqualTo(title));
    }

    [TestCase("enabled: false\ntitles:\n  OperatorXeno: Override")]
    [TestCase("enabled: true\ntitles: {}")]
    public void DisabledOrEmptyConfigurationPreservesOriginalTitle(string configuration)
    {
        Assert.That(Load(configuration).GetTitle(MakeSession("OperatorXeno"), new AdminData { Title = "Original" }), Is.EqualTo("Original"));
    }

    [TestCase("enabled: [")]
    [TestCase("")]
    [TestCase("# Only a comment")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("enabled: maybe\ntitles: {}")]
    [TestCase("enabled: true\ntitles: []")]
    [TestCase("enabled: true\ntitles: {}\nenable: true")]
    [TestCase("enabled: true\nenabled: false\ntitles: {}")]
    [TestCase("enabled: true\ntitles: {}\ntitles: {}")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: One\n  operatorxeno: Two")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: One\n  OperatorXeno: Two")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: ''")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: null")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: ~")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno:")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: [42]")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: {}")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: |\n    Line\n    OtherAdmin")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: One\n  AnotherAdmin: ' '")]
    [TestCase("enabled: true\ntitles:\n  ' OperatorXeno': One")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: One\n AnotherAdmin: Two")]
    [TestCase("enabled: true\ntitles:\n  OperatorXeno: *unknown")]
    [TestCase("enabled: true\ntitles: {}\n---\nenabled: false\ntitles: {}")]
    [TestCase("enabled: true\ntitles: { [OperatorXeno]: One }")]
    public void InvalidConfigurationDisablesEntireFeature(string configuration)
    {
        Assert.That(Load(configuration), Is.SameAs(FixedAdminTitles.Disabled));
    }

    [Test]
    public void MissingUnreadableAndOversizedFilesDisableFeature()
    {
        File.Delete(_path);
        Assert.That(FixedAdminTitles.LoadFromFile(_path, _log.Object), Is.SameAs(FixedAdminTitles.Disabled));
        Assert.That(FixedAdminTitles.LoadFromFile(_directory, _log.Object), Is.SameAs(FixedAdminTitles.Disabled));
        Assert.That(Load(new string(' ', 65537)), Is.SameAs(FixedAdminTitles.Disabled));
    }

    [Test]
    public void FileChangesOnlyAffectNewSnapshots()
    {
        var session = MakeSession("OperatorXeno");
        var admin = new AdminData { Title = "Original" };
        var changed = Load("enabled: true\ntitles:\n  OperatorXeno: Changed");
        Assert.That(_titles.GetTitle(session, admin), Is.EqualTo(FixedTitle));
        Assert.That(changed.GetTitle(session, admin), Is.EqualTo("Changed"));

        var disabled = Load("enabled: false\ntitles: {}");
        Assert.That(_titles.GetTitle(session, admin), Is.EqualTo(FixedTitle));
        Assert.That(disabled.GetTitle(session, admin), Is.EqualTo("Original"));
    }

    private FixedAdminTitles LoadTitle(string title)
    {
        var mapping = new YamlMappingNode
        {
            { "enabled", "true" },
            { "titles", new YamlMappingNode { { "OperatorXeno", new YamlScalarNode(title) { Style = ScalarStyle.DoubleQuoted } } } },
        };
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(mapping)).Save(writer, assignAnchors: false);
        return Load(writer.ToString());
    }

    private FixedAdminTitles Load(string configuration)
    {
        File.WriteAllText(_path, configuration);
        return FixedAdminTitles.LoadFromFile(_path, _log.Object);
    }

    private Mock<IAdminManager> RegisterAdmins(ICommonSession session, Func<AdminData> getAdmin)
    {
        var admins = new Mock<IAdminManager>();
        admins.SetupGet(a => a.ActiveAdmins).Returns(() => getAdmin().Active ? new[] { session } : []);
        admins.Setup(a => a.GetAdminData(session, false)).Returns(getAdmin);
        admins.Setup(a => a.GetAdminTitle(session)).Returns(() => _titles.GetTitle(session, getAdmin()));
        IoCManager.RegisterInstance<IAdminManager>(admins.Object);
        IoCManager.RegisterInstance<IAfkManager>(new Mock<IAfkManager>().Object);
        IoCManager.BuildGraph();
        return admins;
    }

    private static ICommonSession MakeSession(string accountName, string displayName = null)
    {
        var channel = new Mock<INetChannel>();
        channel.SetupGet(c => c.UserName).Returns(accountName);
        var session = new Mock<ICommonSession>();
        session.SetupGet(s => s.Channel).Returns(channel.Object);
        session.SetupGet(s => s.Name).Returns(displayName ?? accountName);
        return session.Object;
    }
}
