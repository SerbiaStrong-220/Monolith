using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using NUnit.Framework;
using Robust.Server.Player;

namespace Content.IntegrationTests.Tests._Exodus.Administration;

[TestFixture]
public sealed class AdminLogExportPermissionsTest
{
    [Test]
    public async Task ExportRequiresBothFlagsAndRevocationCancelsWhileLogsBrowsingRemainsOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
            DummyTicker = false,
            AdminLogsEnabled = true,
        });
        var server = pair.Server;
        AdminLogsEui window = null;
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.Logs;
            window = new AdminLogsEui();
            server.ResolveDependency<EuiManager>().OpenEui(window, player);
        });
        await PoolManager.WaitUntil(server, () => !((AdminLogsEuiState) window.GetNewState()).IsLoading);
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            var exports = server.System<AdminLogExportSystem>();
            var adminData = admins.GetAdminData(player)!;
            var roundId = server.System<GameTicker>().RoundId;
            Assert.That(roundId, Is.GreaterThan(0));

            Assert.That(((AdminLogsEuiState) window.GetNewState()).CanExportRoundLogs, Is.False);
            window.HandleMessage(new AdminLogExportBegin(roundId));
            Assert.That(Challenge(window), Is.Null, "Logs-only access must not even create an export challenge.");
            Assert.That(exports.ActiveCount, Is.Zero);

            adminData.Flags = AdminFlags.Logs | AdminFlags.Admin;
            NotifyPermissionsChanged();
            Assert.That(((AdminLogsEuiState) window.GetNewState()).CanExportRoundLogs, Is.True);
            window.HandleMessage(new AdminLogExportBegin(roundId));
            var challenge = Challenge(window);
            Assert.That(challenge, Is.Not.Null);
            var token = challenge!.Token;
            var id = challenge.Id;

            adminData.Flags = AdminFlags.Logs;
            NotifyPermissionsChanged();
            Assert.Multiple(() =>
            {
                Assert.That(window.IsShutDown, Is.False, "The existing log browser remains authorized.");
                Assert.That(((AdminLogsEuiState) window.GetNewState()).CanExportRoundLogs, Is.False);
                Assert.That(Challenge(window), Is.Null, "Revoking Admin must cancel the pending challenge.");
            });
            window.HandleMessage(new AdminLogExportConfirm(id, 1, token));
            window.HandleMessage(new AdminLogExportBegin(roundId));
            Assert.That(Challenge(window), Is.Null);
            Assert.That(exports.ActiveCount, Is.Zero);

            adminData.Flags = AdminFlags.Admin;
            NotifyPermissionsChanged();
            Assert.That(window.IsShutDown, Is.True, "Losing Logs closes the existing log browser too.");

            // Actual GameTicker needs detachment before destructive shutdown clears PlayerData.
            server.PlayerMan.SetAttachedEntity(player, null);

            void NotifyPermissionsChanged()
            {
                // Use the real manager's event without an unrelated asynchronous database rank reload.
                typeof(AdminManager).GetMethod("SendPermsChangedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(admins, [player]);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    private static AdminLogExportChallenge Challenge(AdminLogsEui window)
    {
        var session = (AdminLogExportSession) typeof(AdminLogsEui)
            .GetField("_exportSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        return session == null ? null : (AdminLogExportChallenge) typeof(AdminLogExportSession)
            .GetField("_challenge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);
    }
}
