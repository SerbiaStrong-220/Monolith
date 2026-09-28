using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._Exodus.Communications.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Content.Shared._Mono.Company;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class FactionConsoleUiTest
{
    [TestCase("ru-RU", 880, 640)]
    [TestCase("en-US", 880, 640)]
    [TestCase("ru-RU", 960, 680)]
    public async Task TabsFitAndOrdinaryLayoutIsRestored(string culture, int width, int height)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            var original = loc.DefaultCulture;
            loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            var menu = new CommunicationsConsoleMenu();
            try
            {
                Configure(menu, pair.Client.ResolveDependency<IPrototypeManager>());
                menu.UpdateWarState(State(incoming: true));
                Assert.That(Descendants(menu.FactionDashboard).OfType<ScrollContainer>(),
                    Is.EquivalentTo(new[] { menu.FactionDashboard.WarScroll, menu.FactionDashboard.CorporationScroll }));
                var tabs = menu.FactionDashboard.FindControl<TabContainer>("Tabs");
                Assert.That(tabs.ChildCount, Is.EqualTo(3));
                menu.SetSize = new Vector2(width, height);
                for (var tab = 0; tab < tabs.ChildCount; tab++)
                {
                    tabs.CurrentTab = tab;
                    if (tab == 0)
                    {
                        var confirm = Descendants(menu.FactionDashboard.WarRows).OfType<ConfirmButton>()
                            .Single(button => button.Visible && !button.Disabled);
                        confirm.IsConfirming = true;
                        menu.UpdateWarState(State(incoming: true));
                    }
                    menu.Measure(new Vector2(width, height));
                    menu.Arrange(new UIBox2(0, 0, width, height));
                    Assert.That(menu.DesiredSize.X, Is.LessThanOrEqualTo(width), $"Tab {tab}");
                    Assert.That(menu.DesiredSize.Y, Is.LessThanOrEqualTo(height), $"Tab {tab}");
                    foreach (var control in Descendants(tabs).Where(control => control.VisibleInTree &&
                                 control is PanelContainer or Button or OptionButton or RichTextLabel or ScrollContainer))
                    {
                        Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(width),
                            $"Tab {tab}: {control.Name ?? control.GetType().Name} must fit horizontally.");
                        if (!IsInsideScrollContainer(control))
                        {
                            Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(height),
                                $"Tab {tab}: {control.Name ?? control.GetType().Name} must fit vertically.");
                        }
                    }
                }

                Assert.That(menu.FactionDashboard.FindControl<RichTextLabel>("FactionCodeDetail").GetMessage(), Is.Not.Empty);
                Assert.That(menu.FactionDashboard.FindControl<RichTextLabel>("FactionTransition").GetMessage(),
                    Does.Contain(loc.GetString("faction-alert-code-pandora")));
                menu.UpdateWarState(null);
                menu.UpdateFactionAlerts(null);
                Assert.That(tabs.VisibleInTree, Is.False);
                Assert.That(menu.MinSize, Is.EqualTo(new Vector2(440, 400)));
                Assert.That(menu.FindControl<TextEdit>("MessageInput").Parent.Name, Is.EqualTo("StandardPanel"));
                Assert.That(menu.AlertLevelButton.Parent.Name, Is.EqualTo("StandardActions"));
            }
            finally
            {
                menu.Close();
                loc.DefaultCulture = original;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SearchValidationAndStaleConfirmations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu { CanAnnounce = true, CanBroadcast = true };
            Configure(menu, pair.Client.ResolveDependency<IPrototypeManager>());
            menu.UpdateWarState(State(incoming: true));
            var tabs = menu.FactionDashboard.FindControl<TabContainer>("Tabs");
            Assert.That(tabs.GetActualTabTitle(0), Does.Contain("1"));
            var confirm = Descendants(menu.FactionDashboard.FindControl<BoxContainer>("WarRows"))
                .OfType<ConfirmButton>().Single(button => button.Visible && !button.Disabled);
            confirm.IsConfirming = true;
            menu.UpdateWarState(State(incoming: true));
            Assert.That(confirm.IsConfirming, Is.True, "An unchanged snapshot must preserve confirmation.");
            menu.UpdateWarState(State(incoming: true, offerId: 2));
            Assert.That(confirm.IsConfirming, Is.False, "A replacement offer must require new confirmation.");
            confirm.IsConfirming = true;
            tabs.CurrentTab = 1;
            Assert.That(confirm.IsConfirming, Is.False, "Leaving diplomacy must cancel confirmation.");

            var message = menu.FindControl<TextEdit>("MessageInput");
            message.TextRope = new Rope.Leaf("   ");
            menu.UpdateAnnouncementAvailability();
            Assert.That(menu.AnnounceButton.Disabled && menu.BroadcastButton.Disabled, Is.True);
            message.TextRope = new Rope.Leaf("Test announcement");
            menu.UpdateAnnouncementAvailability();
            Assert.That(menu.AnnounceButton.Disabled || menu.BroadcastButton.Disabled, Is.False);
            var max = pair.Client.ResolveDependency<IConfigurationManager>().GetCVar(CCVars.ChatMaxAnnouncementLength);
            message.TextRope = new Rope.Leaf(new string('x', max + 1));
            menu.UpdateAnnouncementAvailability();
            Assert.That(menu.AnnounceButton.Disabled && menu.BroadcastButton.Disabled, Is.True);

            tabs.CurrentTab = 2;
            var search = menu.FactionDashboard.FindControl<LineEdit>("CorporationSearch");
            search.SetText("no-such-corporation", true);
            Assert.That(menu.FactionDashboard.FindControl<Label>("CorporationEmpty").Visible, Is.True);
            search.SetText(string.Empty, true);
            Assert.That(menu.FactionDashboard.FindControl<Label>("CorporationEmpty").Visible, Is.False);
            Assert.That(menu.FactionDashboard.FindControl<BoxContainer>("CorporationRows").ChildCount, Is.EqualTo(2));
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            search.SetText(loc.GetString("war-faction-short-TSFMC"), true);
            Assert.That(menu.FactionDashboard.CorporationRows.Children.Count(control => control.Visible), Is.EqualTo(1),
                "Faction abbreviations must find affiliated corporations.");
            menu.UpdateWarState(State());
            Assert.That(tabs.CurrentTab, Is.EqualTo(2), "Server updates must preserve the selected tab.");
            menu.Close();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionAndCorporationListsShowAllEntries()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = State(incoming: true);
        state.Targets.Add(new("Khsira", "territory-faction-khsira-name", WarDeclarationDirection.None,
            TimeSpan.FromHours(2), PeaceOfferDirection.None, 0, TimeSpan.Zero));
        state.Corporations.AddRange([new("MMC", null), new("TheViperGroup", null)]);
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            Configure(menu, pair.Client.ResolveDependency<IPrototypeManager>());
            menu.UpdateWarState(state);
            Assert.That(menu.FactionDashboard.WarRows.Parent, Is.SameAs(menu.FactionDashboard.WarScroll));
            Assert.That(menu.FactionDashboard.WarScroll.HScrollEnabled, Is.False);
            Assert.That(menu.FactionDashboard.WarRows.Children.Count(control => control.Visible), Is.EqualTo(2));
            menu.UpdateWarState(state);
            Assert.That(menu.FactionDashboard.WarRows.Children.Count(control => control.Visible), Is.EqualTo(2));
            menu.FactionDashboard.Tabs.CurrentTab = 2;
            Assert.That(menu.FactionDashboard.CorporationRows.Parent, Is.SameAs(menu.FactionDashboard.CorporationScroll));
            Assert.That(menu.FactionDashboard.CorporationScroll.HScrollEnabled, Is.False);
            Assert.That(menu.FactionDashboard.CorporationRows.Children.Count(control => control.Visible), Is.EqualTo(4));
            menu.UpdateWarState(state);
            Assert.That(menu.FactionDashboard.CorporationRows.Children.Count(control => control.Visible), Is.EqualTo(4));
            ProtoId<CompanyPrototype> companyId = "Augok";
            var company = pair.Client.ResolveDependency<IPrototypeManager>().Index(companyId);
            menu.FactionDashboard.CorporationSearch.SetText(
                pair.Client.ResolveDependency<ILocalizationManager>().GetString(company.Name), true);
            Assert.That(menu.FactionDashboard.CorporationScroll.VScroll, Is.Zero, "Search must return to the top.");
            Assert.That(menu.FactionDashboard.CorporationRows.Children.Count(control => control.Visible), Is.EqualTo(1));
            menu.Close();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WarButtonFollowsTheActiveCodeAndClearsStaleConfirmation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var menu = new CommunicationsConsoleMenu();
            try
            {
                Configure(menu, pair.Client.ResolveDependency<IPrototypeManager>());
                menu.UpdateWarState(State());
                var button = Descendants(menu.FactionDashboard.WarRows).OfType<ConfirmButton>()
                    .Single(control => control.Visible);
                var loc = pair.Client.ResolveDependency<ILocalizationManager>();
                Assert.That(button.Disabled, Is.True, "A pending transition must not unlock war.");
                Assert.That(menu.FactionDashboard.WarUnlockLabel.GetMessage(),
                    Is.EqualTo(loc.GetString("war-declaration-code-restricted")));

                menu.UpdateWarState(State(codeAllowsWar: true));
                Assert.That(button.Disabled, Is.False, "An active non-default code must unlock war immediately.");
                button.IsConfirming = true;
                menu.UpdateWarState(State());
                Assert.That(button.Disabled, Is.True);
                Assert.That(button.IsConfirming, Is.False);
            }
            finally
            {
                menu.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionIconsLoadAtNativeResolution()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var sprites = pair.Client.EntMan.System<SpriteSystem>();
            foreach (var faction in new[] { "TSFMC", "PDV", "Khsira" })
            {
                Assert.That(prototypes.Index<TerritoryFactionPrototype>(faction).Icon, Is.Not.Null);
                var texture = FactionBannerIcon.TryGet(prototypes, sprites, faction);
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture!.Size, Is.EqualTo(new Vector2i(64, 64)));
            }

            Assert.That(FactionBannerIcon.TryGet(prototypes, sprites, "Syndicate"), Is.Not.Null,
                "Factions without UI art must retain their in-world banner.");
        });
        await pair.CleanReturnAsync();
    }

    private static void Configure(CommunicationsConsoleMenu menu, IPrototypeManager prototypes)
    {
        ProtoId<FactionAlertLevelPrototype> codesId = "factionAlerts";
        var codes = prototypes.Index(codesId);
        var levels = codes.Levels.Select(level => new FactionAlertLevelOptionState(
            level.Key, level.Value.Name, level.Value.Description, level.Value.Color, level.Key == "pandora")).ToList();
        menu.UpdateFactionAlerts(new(levels, "hestia", "pandora", TimeSpan.Zero, TimeSpan.FromHours(1)));
        menu.UpdateSectorCode("green", "red", TimeSpan.FromHours(1), Color.Green);
        menu.UpdateAlertLevels(["green", "red"], "green");
    }

    private static WarDeclarationConsoleState State(bool incoming = false, int offerId = 1, bool codeAllowsWar = false)
    {
        return new("TSFMC", "territory-faction-tsf-name", true, codeAllowsWar,
            [new("PDV", "territory-faction-phaeton-name", WarDeclarationDirection.None,
                TimeSpan.Zero, PeaceOfferDirection.None, 0, TimeSpan.Zero,
                AllianceDirection: incoming ? AllianceOfferDirection.Incoming : AllianceOfferDirection.None,
                AllianceOfferId: offerId)],
            [new("TSFMC", "territory-faction-tsf-name", "war-faction-short-TSFMC", Color.Blue),
             new("PDV", "territory-faction-phaeton-name", "war-faction-short-PDV", Color.Gold),
             new("Khsira", "territory-faction-khsira-name", "war-faction-short-Khsira", Color.Pink)],
            [new("TSFMC", "PDV", FactionRelationKind.Neutral),
             new("TSFMC", "Khsira", FactionRelationKind.Alliance),
             new("PDV", "Khsira", FactionRelationKind.War)],
            [new("Augok", "TSFMC"), new("Buno", null)],
            TimeSpan.FromMinutes(20));
    }

    private static bool IsInsideScrollContainer(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ScrollContainer)
                return true;
        }

        return false;
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
