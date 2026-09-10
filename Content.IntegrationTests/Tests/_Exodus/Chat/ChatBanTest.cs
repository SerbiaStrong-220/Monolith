using System.Linq;
using Content.Client._Exodus.Chat;
using Content.Client.Administration.UI.Notes;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Notes;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Chat;
using Content.Shared.Administration;
using Content.Shared.Administration.Notes;
using Content.Shared.Chat;
using Content.Shared.Database._Exodus.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Network;
using ServerBanPanel = Content.Server.Administration.BanPanelEui;
using ClientBanPanel = Content.Client.Administration.UI.BanPanel.BanPanel;
using Content.Shared.Database;

namespace Content.IntegrationTests.Tests._Exodus.Chat;

[TestFixture]
public sealed class ChatBanTest
{
    [Test]
    public async Task LobbyMutePersistsAcrossReconnectAndRoundRestartAndEnforcesPermissions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            InLobby = true,
            Fresh = true,
            Destructive = true, // Ban records survive round cleanup, so this pair must not return to the pool.
        });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var mutes = server.ResolveDependency<IBanManager>();
        var player = pair.Player;
        await server.WaitPost(() => admins.PromoteHost(player));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() => Assert.That(admins.HasAdminFlag(player, AdminFlags.Ban), Is.True));

        ServerBanPanel panel = null;
        await server.WaitPost(async () =>
        {
            var target = await server.ResolveDependency<IPlayerLocator>().LookupIdByNameOrIdAsync(player.UserId.ToString());
            panel = new ServerBanPanel();
            server.ResolveDependency<EuiManager>().OpenEui(panel, player);
            panel.ChangePlayer(target.UserId, target.Username, target.LastAddress, target.LastHWId);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            Assert.That(ui.WindowRoot.Children.OfType<ClientBanPanel>().Count(), Is.EqualTo(1));
        });

        await pair.WaitClientCommand("ooc before_muting");
        await AssertReceived(pair, ChatChannel.OOC, "before_muting", true);
        var request = new Content.Shared.Administration.Ban(player.UserId.ToString(), null, false, null, false, 60,
            "Spam", NoteSeverity.Medium, null, null, false, [BannableChats.OOC], BanType.Chat);
        var invalidRequest = new Content.Shared.Administration.Ban(player.UserId.ToString(), null, false, null, false, 60,
            "Spam", NoteSeverity.Medium, null, null, false, [], BanType.Chat);
        await server.WaitAssertion(() =>
        {
            panel.HandleMessage(new BanPanelEuiStateMsg.CreateBanRequest(invalidRequest));
            Assert.That(((BanPanelEuiState)panel.GetNewState()).Error, Is.Not.Null);
        });
        await server.WaitPost(() => panel.HandleMessage(new BanPanelEuiStateMsg.CreateBanRequest(request)));
        // A repeated packet must not create a second punishment.
        await server.WaitPost(() => panel.HandleMessage(new BanPanelEuiStateMsg.CreateBanRequest(request)));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(((BanPanelEuiState)panel.GetNewState()).Error, Is.Null);
            Assert.That(mutes.CanSendChat(player, ChatChannel.OOC), Is.False);
            Assert.That(mutes.CanSendChat(player, ChatChannel.LOOC), Is.True);
            Assert.That(mutes.CanSendChat(player, ChatChannel.Dead), Is.True);
            Assert.That(mutes.CanSendChat(player, ChatChannel.Admin), Is.True);
        });
        await pair.Client.WaitAssertion(() => Assert.That(pair.Client.ResolveDependency<ChatRequirementsManager>()
            .IsBanned(ChatSelectChannel.OOC), Is.True));
        await pair.WaitClientCommand($"adminnotes {player.UserId}");
        await pair.RunTicksSync(10);
        SharedAdminNote note = null;
        await server.WaitAssertion(async () =>
        {
            var active = await db.GetBansAsync(null, player.UserId, null, null, false, BanType.Chat);
            note = (await db.GetBanAsNoteAsync(active.Single().Id!.Value)).ToShared();
            Assert.That(note.NoteType, Is.EqualTo(NoteType.ChatBan));
        });
        await pair.Client.WaitAssertion(() =>
        {
            var edit = new NoteEdit(note, player.Name, true, true);
            edit.OpenCentered();
            edit.Close();
            var popup = new AdminNotesLinePopup(note, player.Name, true, true);
            Assert.That(popup.FindControl<Content.Client.UserInterface.Controls.ConfirmButton>("ChatUnbanButton").Visible, Is.True);
            popup.Close();
        });
        await pair.WaitClientCommand("ooc blocked_in_lobby");
        await AssertReceived(pair, ChatChannel.OOC, "blocked_in_lobby", false);

        var userId = player.UserId;
        var username = player.Name;
        await pair.Disconnect();
        // The default test Connect() creates a different account on each connection.
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>()
            .ClientConnect("localhost", 0, username));
        await pair.ReallyBeIdle(10);
        player = pair.Player;
        await server.WaitAssertion(() =>
        {
            Assert.That(player.UserId, Is.EqualTo(userId));
            Assert.That(mutes.CanSendChat(player, ChatChannel.OOC), Is.False);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() => Assert.That(mutes.CanSendChat(player, ChatChannel.OOC), Is.False));

        await server.WaitPost(() => admins.DeAdmin(player));
        await server.WaitAssertion(async () => Assert.That(await mutes.TryCreateChatsBanAsync(NewInfo(player.UserId, player.Name, BannableChats.OOC), player), Is.Null));
        await server.WaitPost(() => admins.ReAdmin(player));
        await server.WaitAssertion(async () =>
        {
            var active = await db.GetBansAsync(null, player.UserId, null, null, false, BanType.Chat);
            Assert.That(active, Has.Count.EqualTo(1));
            Assert.That(await mutes.TryPardonChatsBanAsync(active[0].Id!.Value, player), Is.True);
            Assert.That(mutes.CanSendChat(player, ChatChannel.OOC), Is.True);
        });
        await pair.WaitClientCommand("ooc after_unmuting");
        await AssertReceived(pair, ChatChannel.OOC, "after_unmuting", true);
        await server.WaitAssertion(async () =>
        {
            var info = NewInfo(player.UserId, player.Name, BannableChats.OOC);
            info.WithDuration(TimeSpan.FromSeconds(1));
            Assert.That(await mutes.TryCreateChatsBanAsync(info, player), Is.Not.Null);
            Assert.That(mutes.IsChatBanned(player, BannableChats.OOC), Is.True);
        });
        // Database ban times are wall-clock timestamps. Wait off the server thread.
        await Task.Delay(1100);
        await pair.RunTicksSync(40);
        await server.WaitAssertion(() => Assert.That(mutes.IsChatBanned(player, BannableChats.OOC), Is.False));
        await pair.Client.WaitAssertion(() => Assert.That(pair.Client.ResolveDependency<ChatRequirementsManager>()
            .IsBanned(ChatSelectChannel.OOC), Is.False));
        await pair.WaitClientCommand("ooc expired_mid_round");
        await AssertReceived(pair, ChatChannel.OOC, "expired_mid_round", true);
        // Exercise command parsing, including multiple channels and the Ghost alias.
        await pair.WaitClientCommand($"chatban {player.Name} OOC,Ghost \"Command test\" 60");
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(mutes.IsChatBanned(player, BannableChats.OOC), Is.True);
            Assert.That(mutes.IsChatBanned(player, BannableChats.Dead), Is.True);
        });
        await pair.Disconnect();
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostSpeechAndLoocCannotBypassTheirChannelMutes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
            Fresh = true,
            Destructive = true, // Ban records survive round cleanup, so this pair must not return to the pool.
        });
        var server = pair.Server;
        var admins = server.ResolveDependency<IAdminManager>();
        var mutes = server.ResolveDependency<IBanManager>();
        var player = pair.Player;
        await server.WaitPost(() => admins.PromoteHost(player));
        await pair.RunTicksSync(10);
        await pair.WaitClientCommand("looc before_looc_mute");
        await AssertReceived(pair, ChatChannel.LOOC, "before_looc_mute", true);
        await server.WaitAssertion(async () => Assert.That(await mutes.TryCreateChatsBanAsync(NewInfo(player.UserId, player.Name, BannableChats.LOOC), player), Is.Not.Null));
        await pair.WaitClientCommand("looc blocked_looc");
        await AssertReceived(pair, ChatChannel.LOOC, "blocked_looc", false);

        await pair.WaitClientCommand("aghost");
        await pair.WaitClientCommand("say before_ghost_mute");
        await AssertReceived(pair, ChatChannel.Dead, "before_ghost_mute", true);
        await server.WaitAssertion(async () => Assert.That(await mutes.TryCreateChatsBanAsync(NewInfo(player.UserId, player.Name, BannableChats.Dead), player), Is.Not.Null));
        await pair.WaitClientCommand("say blocked_ghost_say");
        await pair.WaitClientCommand("dsay blocked_ghost_dsay");
        await AssertReceived(pair, ChatChannel.Dead, "blocked_ghost_say", false);
        await AssertReceived(pair, ChatChannel.Dead, "blocked_ghost_dsay", false);

        await server.WaitPost(() => admins.DeAdmin(player));
        await pair.WaitClientCommand("looc blocked_redirected_looc");
        await AssertReceived(pair, ChatChannel.Dead, "blocked_redirected_looc", false);
        await pair.Disconnect();
        await pair.CleanReturnAsync();
    }

    private static CreateChatsBanInfo NewInfo(NetUserId user, string name, params BannableChats[] chats)
    {
        var info = new CreateChatsBanInfo("Chat spam");
        info.AddUser(user, name);
        info.WithMinutes(60);
        foreach (var chat in chats)
            info.AddChat(chat);
        return info;
    }

    private static async Task AssertReceived(Pair.TestPair pair, ChatChannel channel, string message, bool expected)
    {
        await pair.Client.WaitAssertion(() =>
        {
            var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            Assert.That(chat.History.Any(entry => entry.Msg.Channel == channel && entry.Msg.Message == message),
                Is.EqualTo(expected), $"Unexpected delivery of {channel} message '{message}'");
        });
    }
}
