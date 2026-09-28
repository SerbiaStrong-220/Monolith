using System.Collections.Generic;
using System.Numerics;
using Content.Client._Exodus.Communications.UI;
using Content.Server._Exodus.War;
using Content.Server._NF.SectorServices;
using Content.Server.AlertLevel;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Communications;
using Content.Shared._Exodus.War;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class FactionCodesTest
{
    [Test]
    public async Task OnlyNextFactionCodeCanBeSelectedOnServerAndClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true });
        string[] codes = ["hestia", "pandora", "ares"];
        var snapshots = new List<FactionAlertLevelState>();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var faction = entities.System<FactionAlertLevelSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            Assert.That(faction.TryGetState(out var state), Is.True);
            state.Comp.RoundInitialized = true;
            for (var current = 0; current < codes.Length; current++)
            {
                state.Comp.CurrentLevel = codes[current];
                state.Comp.NextChangeAt = timing.CurTime;
                Assert.That(faction.TryCopyState(out var snapshot), Is.True);
                snapshots.Add(snapshot);
                Assert.That(snapshot.Levels.Count, Is.EqualTo(codes.Length));
                for (var target = 0; target < codes.Length; target++)
                {
                    state.Comp.CurrentLevel = codes[current];
                    state.Comp.NextChangeAt = timing.CurTime;
                    var isNext = target == current + 1;
                    Assert.That(snapshot.Levels[target].Id, Is.EqualTo(codes[target]));
                    Assert.That(snapshot.Levels[target].Selectable, Is.EqualTo(isNext));
                    var deadline = state.Comp.PendingAt;
                    Assert.That(faction.TrySetLevel(codes[target], out var result), Is.EqualTo(isNext),
                        $"{codes[current]} -> {codes[target]}");
                    Assert.That(state.Comp.CurrentLevel, Is.EqualTo(codes[current]));
                    if (!isNext)
                    {
                        Assert.That(result, Is.EqualTo(current == target
                            ? FactionAlertLevelSetResult.AlreadyActive : FactionAlertLevelSetResult.NotNextLevel));
                        Assert.That(state.Comp.PendingLevel, Is.Null);
                        Assert.That(state.Comp.PendingAt, Is.EqualTo(deadline));
                        Assert.That(state.Comp.NextChangeAt, Is.EqualTo(timing.CurTime));
                        continue;
                    }

                    Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.Success));
                    Assert.That(state.Comp.PendingLevel, Is.EqualTo(codes[target]));
                    state.Comp.PendingAt = timing.CurTime;
                    faction.Update(0);
                    Assert.That(state.Comp.CurrentLevel, Is.EqualTo(codes[target]));
                    Assert.That(state.Comp.PendingLevel, Is.Null);
                    Assert.That(state.Comp.NextChangeAt - timing.CurTime, Is.EqualTo(TimeSpan.FromHours(1)));
                }
            }

            entities.DeleteEntity(host);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            try
            {
                foreach (var snapshot in snapshots)
                {
                    menu.UpdateFactionAlerts(snapshot);
                    Assert.That(menu.FactionAlertLevelButton.SelectedMetadata, Is.EqualTo(snapshot.CurrentLevel));
                    for (var i = 0; i < codes.Length; i++)
                    {
                        Assert.That(menu.FactionAlertLevelButton.GetItemMetadata(i), Is.EqualTo(codes[i]));
                        Assert.That(menu.FactionAlertLevelButton.IsItemDisabled(i),
                            Is.EqualTo(!snapshot.Levels[i].Selectable));
                    }
                }
            }
            finally
            {
                menu.Close();
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarDeclarationsRequireAnActiveNonDefaultCode()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var faction = entities.System<FactionAlertLevelSystem>();
            var wars = entities.System<FactionWarSystem>();
            var ticker = entities.System<GameTicker>();
            var timing = (GameTiming) server.ResolveDependency<IGameTiming>();
            var timeBase = timing.TimeBase;
            try
            {
                Assert.That(faction.TryGetState(out var state), Is.True);
                Assert.That(wars.CodeAllowsWar(), Is.False);
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.CodeRestricted));

                // Round age must never unlock war while the starting code is still active.
                timing.TimeBase = (timeBase.Item1 + TimeSpan.FromHours(3), timeBase.Item2);
                Assert.That(ticker.RoundDuration(), Is.GreaterThanOrEqualTo(TimeSpan.FromHours(3)));
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.CodeRestricted));
                timing.TimeBase = timeBase;

                state.Comp.RoundInitialized = true;
                state.Comp.NextChangeAt = timing.CurTime;
                Assert.That(faction.TrySetLevel("pandora", out _), Is.True);
                Assert.That(wars.CodeAllowsWar(), Is.False);
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.CodeRestricted));
                state.Comp.PendingAt = timing.CurTime + TimeSpan.FromTicks(1);
                faction.Update(0);
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.CodeRestricted));

                state.Comp.PendingAt = timing.CurTime;
                faction.Update(0);
                Assert.That(ticker.RoundDuration(), Is.LessThan(TimeSpan.FromHours(2)));
                Assert.That(wars.CodeAllowsWar(), Is.True);
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));

                state.Comp.NextChangeAt = timing.CurTime;
                Assert.That(faction.TrySetLevel("ares", out _), Is.True);
                Assert.That(wars.TryDeclareWar("TSFMC", "Khsira"), Is.EqualTo(WarDeclarationResult.Success),
                    "Pandora continues to permit declarations during the transition to Ares.");
                state.Comp.PendingAt = timing.CurTime;
                faction.Update(0);
                Assert.That(wars.CodeAllowsWar(), Is.True);
                Assert.That(wars.TryDeclareWar("PDV", "Khsira"), Is.EqualTo(WarDeclarationResult.Success));
            }
            finally
            {
                timing.TimeBase = timeBase;
                entities.DeleteEntity(host);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RedSectorCodeRequiresForceAndStillDisplaysWhenActive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var selectable = new List<string>();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var service = entities.System<SectorServiceSystem>().GetServiceEntity();
            var alerts = entities.System<AlertLevelSystem>();
            var state = entities.GetComponent<AlertLevelComponent>(service);
            foreach (var (id, detail) in state.AlertLevels.Levels)
            {
                if (detail.Selectable)
                    selectable.Add(id);
            }

            Assert.That(selectable, Does.Not.Contain("red"));
            alerts.SetLevel(service, "red", false, false);
            Assert.That(state.CurrentLevel, Is.EqualTo("green"));
            Assert.That(state.PendingLevel, Is.Null);
            alerts.SetLevel(service, "red", false, false, force: true);
            Assert.That(state.CurrentLevel, Is.EqualTo("red"));
            Assert.That(state.PendingLevel, Is.Null);
            alerts.SetLevel(service, "green", false, false);
            Assert.That(state.PendingLevel, Is.EqualTo("green"), "Red must not lock the other sector codes.");
            entities.DeleteEntity(host);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            try
            {
                menu.UpdateAlertLevels(selectable, "green");
                Assert.That(menu.AlertLevelButton.ItemCount, Is.EqualTo(selectable.Count));
                menu.UpdateAlertLevels(selectable, "red");
                Assert.That(menu.AlertLevelButton.SelectedMetadata, Is.EqualTo("red"));
                Assert.That(menu.AlertLevelButton.IsItemDisabled(menu.AlertLevelButton.SelectedId), Is.True);
                Assert.That(menu.AlertLevelButton.ItemCount, Is.EqualTo(selectable.Count + 1));
                menu.UpdateAlertLevels(selectable, "green");
                Assert.That(menu.AlertLevelButton.SelectedMetadata, Is.EqualTo("green"));
                Assert.That(menu.AlertLevelButton.ItemCount, Is.EqualTo(selectable.Count));
            }
            finally
            {
                menu.Close();
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CodesTransitionIndependentlyAndRespectTheirMinimumDuration()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var faction = entities.System<FactionAlertLevelSystem>();
            var alerts = entities.System<AlertLevelSystem>();
            var ticker = entities.System<GameTicker>();
            var timing = server.ResolveDependency<IGameTiming>();
            Assert.That(faction.TryGetState(out var state), Is.True);
            var sector = entities.GetComponent<AlertLevelComponent>(state.Owner);
            Assert.That(faction.TryCopyState(out var snapshot), Is.True);
            Assert.That(snapshot.CurrentLevel, Is.EqualTo("hestia"));
            Assert.That(snapshot.AvailableAt - ticker.RoundStartTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(45)));
            Assert.That(faction.TrySetLevel("pandora", out var result), Is.False);
            Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.Cooldown));

            state.Comp.NextChangeAt = timing.CurTime;
            Assert.That(faction.TrySetLevel("pandora", out _), Is.True);
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("hestia"));
            Assert.That(state.Comp.PendingAt - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(15)));
            var pendingAt = state.Comp.PendingAt;
            Assert.That(faction.TrySetLevel("ares", out result), Is.False);
            Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.TransitionInProgress));
            Assert.That(state.Comp.PendingAt, Is.EqualTo(pendingAt));

            alerts.SetLevel(state.Owner, "blue", false, false);
            Assert.That(sector.CurrentLevel, Is.EqualTo("green"));
            Assert.That(sector.PendingLevel, Is.EqualTo("blue"));
            Assert.That(alerts.GetAlertLevelDelay(state.Owner), Is.EqualTo(300));
            sector.PendingAt = timing.CurTime;
            alerts.Update(0);
            Assert.That(sector.CurrentLevel, Is.EqualTo("blue"));
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("hestia"));

            state.Comp.PendingAt = timing.CurTime;
            faction.Update(0);
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("pandora"));
            Assert.That(state.Comp.NextChangeAt - timing.CurTime, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(faction.TrySetLevel("ares", out result), Is.False);
            Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.Cooldown));
            Assert.That(faction.TrySetLevel("hestia", out result), Is.False);
            Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.Cooldown));

            state.Comp.NextChangeAt = timing.CurTime;
            Assert.That(faction.TrySetLevel("ares", out _), Is.True);
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("pandora"));
            Assert.That(state.Comp.PendingAt - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(20)));
            faction.Update(0);
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("pandora"));
            state.Comp.PendingAt = timing.CurTime;
            faction.Update(0);
            Assert.That(state.Comp.CurrentLevel, Is.EqualTo("ares"));
            Assert.That(state.Comp.NextChangeAt - timing.CurTime, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(faction.TrySetLevel("pandora", out result), Is.False);
            Assert.That(result, Is.EqualTo(FactionAlertLevelSetResult.Cooldown));
            state.Comp.NextChangeAt = timing.CurTime;
            Assert.That(faction.TryCopyState(out snapshot), Is.True);
            foreach (var level in snapshot.Levels)
                Assert.That(level.Selectable, Is.False);
            Assert.That(faction.TrySetLevel("kronos", out _), Is.False);
            Assert.That(faction.TrySetLevel("unknown", out _), Is.False);
            Assert.That(faction.TrySetLevel(null!, out _), Is.False);
            entities.DeleteEntity(host);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForceCanCancelAPendingSectorCodeAndLockedLevelsStayLocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var service = entities.System<SectorServiceSystem>().GetServiceEntity();
            var alerts = entities.System<AlertLevelSystem>();
            var state = entities.GetComponent<AlertLevelComponent>(service);
            alerts.SetLevel(service, "blue", false, false);
            Assert.That(state.PendingLevel, Is.EqualTo("blue"));
            alerts.SetLevel(service, "violet", false, false);
            Assert.That(state.PendingLevel, Is.EqualTo("blue"), "A second request must not replace the transition.");
            alerts.SetLevel(service, "green", false, false, force: true);
            Assert.That(state.PendingLevel, Is.Null);
            Assert.That(alerts.GetAlertLevelDelay(service), Is.Zero);

            alerts.SetLevel(service, "gamma", false, false, force: true);
            alerts.SetLevel(service, "blue", false, false);
            Assert.That(state.CurrentLevel, Is.EqualTo("gamma"));
            Assert.That(state.PendingLevel, Is.Null);
            entities.DeleteEntity(host);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionCodeRequiresDiplomaticAccess()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var faction = entities.System<FactionAlertLevelSystem>();
            Assert.That(faction.TryGetState(out var state), Is.True);
            state.Comp.RoundInitialized = true;
            state.Comp.NextChangeAt = TimeSpan.Zero;
            var console = entities.Spawn("TSFComputerComms");
            var actor = entities.Spawn();
            var message = new CommunicationsConsoleSelectFactionAlertLevelMessage("pandora")
            {
                Actor = actor,
                UiKey = CommunicationsConsoleUiKey.Key,
            };
            entities.EventBus.RaiseLocalEvent(console, message);
            Assert.That(state.Comp.PendingLevel, Is.Null);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
            entities.DeleteEntity(host);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsolePanelsFitAndOrdinaryConsolesKeepASingleSelector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            menu.UpdateWarState(null);
            menu.UpdateFactionAlerts(null);
            Assert.That(menu.FindControl<BoxContainer>("StandardPanel").Visible, Is.True);
            Assert.That(menu.FactionDashboard.FindControl<TabContainer>("Tabs").VisibleInTree, Is.False);
            Assert.That(menu.FactionAlertLevelButton.Visible, Is.False);
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            ProtoId<FactionAlertLevelPrototype> codesId = "factionAlerts";
            var codes = prototypes.Index(codesId);
            var levels = new List<FactionAlertLevelOptionState>();
            foreach (var (id, detail) in codes.Levels)
                levels.Add(new(id, detail.Name, detail.Description, detail.Color, id == "pandora"));

            menu.UpdateFactionAlerts(new(levels, "hestia", "pandora", TimeSpan.Zero, TimeSpan.FromHours(2)));
            menu.UpdateSectorCode("red", "violet", TimeSpan.FromHours(2), Color.Red);
            menu.UpdateWarState(new("TSFMC", "territory-faction-tsf-name", true, false,
                [new("PDV", "territory-faction-phaeton-name", WarDeclarationDirection.None,
                    TimeSpan.FromHours(2), PeaceOfferDirection.None, 0, TimeSpan.Zero,
                    LockUntil: TimeSpan.FromHours(2), LockReason: WarLockReason.AllianceBreak),
                 new("Khsira", "territory-faction-khsira-name", WarDeclarationDirection.None,
                    TimeSpan.Zero, PeaceOfferDirection.None, 0, TimeSpan.Zero, FactionRelationKind.Alliance)],
                [new("TSFMC", "territory-faction-tsf-name", "war-faction-short-TSFMC", Color.Blue),
                 new("PDV", "territory-faction-phaeton-name", "war-faction-short-PDV", Color.Red),
                 new("Khsira", "territory-faction-khsira-name", "war-faction-short-Khsira", Color.Gold)],
                [new("TSFMC", "PDV", FactionRelationKind.Neutral),
                 new("TSFMC", "Khsira", FactionRelationKind.Alliance),
                 new("PDV", "Khsira", FactionRelationKind.War)],
                [new("TSFCivilian", "TSFMC"), new("PDVCivilian", null)],
                TimeSpan.FromMinutes(20)));
            menu.FactionDashboard.FindControl<TabContainer>("Tabs").CurrentTab = 1;
            menu.Measure(new Vector2(1100, 720));
            Assert.That(menu.DesiredSize.X, Is.LessThanOrEqualTo(1100), "Text must not expand the console beyond the screen.");
            Assert.That(menu.DesiredSize.Y, Is.LessThanOrEqualTo(720));
            Assert.That(menu.FactionAlertLevelButton.Visible, Is.True);
            Assert.That(menu.FactionDashboard.FindControl<RichTextLabel>("FactionCodeDetail").GetMessage(), Is.Not.Empty);
            menu.Close();
        });
        await pair.CleanReturnAsync();
    }
}
