using System;
using System.Linq;
using System.Reflection;
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.Economy.Admin;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Moq;
using NUnit.Framework;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Player;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminPermissionTest
{
    [TestCase(AdminFlags.None, true, false)]
    [TestCase(AdminFlags.Admin, true, false)]
    [TestCase(AdminFlags.EconomyDB, true, true)]
    [TestCase(AdminFlags.Admin | AdminFlags.EconomyDB, true, true)]
    [TestCase(AdminFlags.EconomyDB, false, false)]
    public void EconomyAccessRequiresItsOwnActiveFlag(AdminFlags flags, bool active, bool allowed)
    {
        var data = new AdminData { Flags = flags, Active = active };
        var player = new Mock<ICommonSession>().Object;
        var admins = new Mock<IAdminManager>(MockBehavior.Strict);
        admins.Setup(manager => manager.HasAdminFlag(player, It.IsAny<AdminFlags>(), false))
            .Returns((ICommonSession _, AdminFlags required, bool includeDeAdmin) => data.HasFlag(required, includeDeAdmin));
        var system = new MarketAdminSystem();
        typeof(MarketAdminSystem).GetField("_admins", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(system, admins.Object);

        Assert.That(system.CanAdminister(player), Is.EqualTo(allowed));
        if (!allowed)
            Assert.That(system.TryOpen(player), Is.False);
        Assert.That(system.TryOpen(null), Is.False, "A console without a player cannot open a confirmation window.");
        Assert.That(system.CanMutateQuotes(null, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo("economy-admin-error-permission"));
    }

    [Test]
    public void EconomyFlagIsAssignableAndRoundTripsWithoutGrantingItToExistingAdmins()
    {
        Assert.That(AdminFlagsHelper.AllFlags.Count(flag => flag == AdminFlags.EconomyDB), Is.EqualTo(1));
        Assert.That(AdminFlagsHelper.FlagsToNames(AdminFlags.EconomyDB), Is.EqualTo(new[] { "ECONOMYDB" }));
        Assert.That(AdminFlagsHelper.NamesToFlags(new[] { "ECONOMYDB" }), Is.EqualTo(AdminFlags.EconomyDB));
        Assert.That(AdminFlagsHelper.NamesToFlags(new[] { "ADMIN", "DEBUG" }) & AdminFlags.EconomyDB,
            Is.EqualTo(AdminFlags.None));
    }

    [TestCase(typeof(MarketAdminCommand))]
    [TestCase(typeof(MarketSetCommand))]
    [TestCase(typeof(MarketResetCommand))]
    [TestCase(typeof(MarketQuoteCommand))]
    [TestCase(typeof(MarketListCommand))]
    public void EconomyCommandsUseTheIndependentPermission(Type command)
    {
        Assert.That(command.GetCustomAttributes<AdminCommandAttribute>().Select(attribute => attribute.Flags),
            Is.EqualTo(new[] { AdminFlags.EconomyDB }));
    }

    [TestCase(typeof(MarketSetCommand), new[] { "stack:Steel", "2" })]
    [TestCase(typeof(MarketResetCommand), new[] { "stack:Steel" })]
    [TestCase(typeof(MarketResetCommand), new string[] { })]
    public void ConsoleMutationsWithoutAPlayerCannotReachTheMarket(Type commandType, string[] args)
    {
        IoCManager.InitThread();
        IoCManager.Clear();
        try
        {
            var localization = new Mock<ILocalizationManager>();
            localization.Setup(manager => manager.GetString("economy-admin-reset-console")).Returns("player required");
            IoCManager.RegisterInstance<ILocalizationManager>(localization.Object);
            IoCManager.BuildGraph();
            var shell = new Mock<IConsoleShell>();
            var command = (IConsoleCommand) Activator.CreateInstance(commandType)!;

            // No entity manager or database exists: the command must reject the missing player first.
            command.Execute(shell.Object, string.Join(' ', args), args);
            shell.Verify(output => output.WriteError("player required"), Times.Once);
        }
        finally
        {
            IoCManager.Clear();
        }
    }
}
