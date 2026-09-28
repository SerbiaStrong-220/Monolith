using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._Exodus.War;
using Content.Shared._Mono.Company;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Communications.UI;

public sealed partial class CommunicationsConsoleMenu
{
    // Keep the ordinary console's controls and behavior; only faction consoles use the dashboard.
    private void SetFactionLayout(bool enabled)
    {
        if (_factionLayout == enabled)
            return;

        _factionLayout = enabled;
        StandardPanel.Visible = !enabled;
        FactionDashboard.Visible = enabled;
        Reparent(MessageInput, enabled ? FactionDashboard.MessageHost : StandardPanel);
        Reparent(AnnounceButton, enabled ? FactionDashboard.AnnouncementActions : StandardActions);
        Reparent(BroadcastButton, enabled ? FactionDashboard.AnnouncementActions : StandardActions);
        Reparent(AlertLevelButton, enabled ? FactionDashboard.SectorSelectorHost : StandardActions);
        if (enabled)
        {
            MinSize = new Vector2(880, 640);
            SetSize = new Vector2(960, 680);
            Title = Loc.GetString("faction-console-title");
            AnnounceButton.RemoveStyleClass("OpenLeft");
            BroadcastButton.RemoveStyleClass("OpenBoth");
            AlertLevelButton.RemoveStyleClass("OpenRight");
            AnnounceButton.AddStyleClass(StyleNano.StyleClassButtonColorGreen);
            AnnounceButton.MinHeight = BroadcastButton.MinHeight = AlertLevelButton.MinHeight = 36;
            AnnounceButton.HorizontalExpand = BroadcastButton.HorizontalExpand = AlertLevelButton.HorizontalExpand = true;
        }
        else
        {
            if (_messageScrollBar != null)
                _messageScrollBar.Visible = true;
            MessageInput.SetPositionInParent(0);
            AnnounceButton.SetPositionInParent(0);
            BroadcastButton.SetPositionInParent(1);
            MinSize = new Vector2(440, 400);
            SetSize = MinSize;
            Title = Loc.GetString("comms-console-menu-title");
            AnnounceButton.RemoveStyleClass(StyleNano.StyleClassButtonColorGreen);
            AnnounceButton.StyleClasses.Add("OpenLeft");
            BroadcastButton.StyleClasses.Add("OpenBoth");
            AlertLevelButton.StyleClasses.Add("OpenRight");
            AnnounceButton.MinHeight = BroadcastButton.MinHeight = AlertLevelButton.MinHeight = 0;
            AnnounceButton.HorizontalExpand = BroadcastButton.HorizontalExpand = AlertLevelButton.HorizontalExpand = false;
        }
    }

    private static void Reparent(Control control, Control parent)
    {
        control.Orphan();
        parent.AddChild(control);
    }

    private void RebuildLegend(WarDeclarationConsoleState state)
    {
        FactionDashboard.FactionLegend.RemoveAllChildren();
        foreach (var pair in state.PairRelations)
        {
            if (!_prototypes.TryIndex(pair.First, out var first) || !_prototypes.TryIndex(pair.Second, out var second))
                continue;

            var label = new RichTextLabel();
            label.SetMessage(Loc.GetString("faction-console-relation-pair",
                ("first", _loc.TryGetString($"war-faction-short-{first.ID}", out var firstName) ? firstName : Loc.GetString(first.RadarLabel)),
                ("second", _loc.TryGetString($"war-faction-short-{second.ID}", out var secondName) ? secondName : Loc.GetString(second.RadarLabel)),
                ("relation", RelationName(pair.Relation))), RelationColor(pair.Relation));
            FactionDashboard.FactionLegend.AddChild(label);
        }
    }

    private void RebuildCorporations(WarDeclarationConsoleState state)
    {
        FactionDashboard.CorporationRows.RemoveAllChildren();
        _corporationRows.Clear();
        var companies = new List<(string Name, CompanyPrototype Company, string Allegiance, string ShortName, Texture? Banner)>();
        foreach (var corporation in state.Corporations)
        {
            if (!_prototypes.TryIndex(corporation.Company, out var company))
                continue;

            var allegiance = Loc.GetString("faction-console-unclaimed");
            var shortName = string.Empty;
            Texture? banner = null;
            if (corporation.Faction is { } faction && _prototypes.TryIndex(faction, out var prototype))
            {
                allegiance = Loc.GetString(prototype.DisplayName ?? prototype.RadarLabel);
                banner = FactionBannerIcon.TryGet(_prototypes, _sprites, faction);
                if (_loc.TryGetString($"war-faction-short-{faction.Id}", out var abbreviation))
                    shortName = abbreviation;
            }

            companies.Add((Loc.GetString(company.Name), company, allegiance, shortName, banner));
        }

        companies.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var (name, company, allegiance, shortName, banner) in companies)
        {
            var card = new PanelContainer { StyleClasses = { "BackgroundDark" } };
            var line = new BoxContainer { SeparationOverride = 12, Margin = new Thickness(10, 6) };
            var emblem = company.LobbyImage is { } icon ? _sprites.Frame0(icon) : null;
            line.AddChild(new TextureRect
            {
                Texture = emblem, SetSize = new Vector2(64, 64),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
            });
            var description = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true, VerticalAlignment = VAlignment.Center, SeparationOverride = 4,
            };
            var title = new Label { Text = name, ClipText = true, ToolTip = name };
            description.AddChild(title);
            var owner = new Label
            {
                Text = Loc.GetString("faction-console-allegiance", ("faction", allegiance)),
                ClipText = true, ToolTip = allegiance, FontColorOverride = Color.LightGray,
            };
            description.AddChild(owner);
            line.AddChild(description);
            line.AddChild(new TextureRect
            {
                Texture = banner, SetSize = new Vector2(48, 48), ToolTip = allegiance,
                Stretch = TextureRect.StretchMode.KeepAspectCentered, VerticalAlignment = VAlignment.Center,
            });
            card.AddChild(line);
            FactionDashboard.CorporationRows.AddChild(card);
            _corporationRows.Add((card, $"{name} {allegiance} {shortName}"));
        }

        FilterCorporations();
    }

    private void FilterCorporations()
    {
        var search = FactionDashboard.CorporationSearch.Text.Trim();
        var count = 0;
        foreach (var (control, text) in _corporationRows)
        {
            control.Visible = text.Contains(search, StringComparison.CurrentCultureIgnoreCase);
            if (control.Visible)
                count++;
        }

        FactionDashboard.CorporationCount.Text = Loc.GetString("faction-console-corporation-count", ("count", count));

        FactionDashboard.CorporationEmpty.Visible = count == 0;
        FactionDashboard.CorporationEmpty.Text = Loc.GetString(_corporationRows.Count == 0
            ? "comms-console-no-corporations" : "faction-console-search-empty");
    }

    private void UpdateCodeReadout(TimeSpan now)
    {
        if (!_hasWarState)
            return;

        if (!_sectorColorReady || _shownSectorColor != _sectorColor)
        {
            _shownSectorColor = _sectorColor;
            _sectorColorReady = true;
            FactionDashboard.SectorColorBar.PanelOverride = new StyleBoxFlat { BackgroundColor = _sectorColor };
        }

        var sectorName = AlertName(_sectorLevel);
        FactionDashboard.SectorSummary.Text = Loc.GetString("faction-console-sector-summary", ("code", sectorName));
        FactionDashboard.SectorSummary.FontColorOverride = _sectorColor;
        FactionDashboard.SectorTransition.Visible = _pendingSector != null;
        if (_pendingSector is { } pendingSector)
            SetTransition(FactionDashboard.SectorTransition, AlertName(pendingSector), _pendingSectorAt - now);

        FactionDashboard.SectorCodeDetail.SetMessage(_loc.TryGetString($"alert-level-{_sectorLevel}-instructions", out var detail)
            ? detail : string.Empty);
        FactionDashboard.SectorCodeCooldown.Visible = !AlertLevelSelectable && _pendingSector == null;
        FactionDashboard.SectorCodeCooldown.SetMessage(Loc.GetString("faction-console-code-unavailable"), Color.LightGray);
        FactionDashboard.FactionSummary.Visible = _factionAlerts != null;
        if (_factionAlerts is not { } faction)
            return;

        if (TryGetFactionLevel(faction.CurrentLevel, out var current))
        {
            if (!_factionColorReady || _shownFactionColor != current.Color)
            {
                _shownFactionColor = current.Color;
                _factionColorReady = true;
                FactionDashboard.FactionColorBar.PanelOverride = new StyleBoxFlat { BackgroundColor = current.Color };
            }

            var name = Loc.GetString(current.Name);
            FactionDashboard.FactionCodeDetail.SetMessage(Loc.GetString(current.Description));
            FactionDashboard.FactionSummary.Text = Loc.GetString("faction-console-faction-summary", ("code", name));
            FactionDashboard.FactionSummary.FontColorOverride = current.Color;
        }

        FactionDashboard.FactionTransition.Visible = faction.PendingLevel != null;
        FactionDashboard.FactionCodeCooldown.Visible = faction.PendingLevel == null &&
            (!_warRoundRunning || now < faction.AvailableAt);
        if (faction.PendingLevel is { } pendingId && TryGetFactionLevel(pendingId, out var pending))
            SetTransition(FactionDashboard.FactionTransition, Loc.GetString(pending.Name), faction.PendingAt - now);

        if (FactionDashboard.FactionCodeCooldown.Visible)
        {
            FactionDashboard.FactionCodeCooldown.SetMessage(_warRoundRunning && faction.AvailableAt != TimeSpan.MaxValue
                ? Loc.GetString("comms-console-code-available-in", ("time", FormatRemaining(faction.AvailableAt - now)))
                : Loc.GetString("faction-alert-round-not-running"), Color.Gold);
        }
    }

    private static void SetTransition(RichTextLabel label, string target, TimeSpan remaining)
    {
        label.SetMessage(Loc.GetString("faction-console-code-transition",
            ("target", target), ("time", FormatRemaining(remaining))), Color.Gold);
        label.ToolTip = Loc.GetString("faction-console-code-transition-hint");
    }

    private bool TryGetFactionLevel(string id, out FactionAlertLevelOptionState level)
    {
        if (_factionAlerts != null)
        {
            foreach (var candidate in _factionAlerts.Levels)
            {
                if (candidate.Id != id)
                    continue;

                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    private static Color RelationColor(FactionRelationKind relation)
    {
        return relation switch
        {
            FactionRelationKind.War => Color.Salmon,
            FactionRelationKind.Alliance => Color.LightGreen,
            _ => Color.LightGray,
        };
    }

    private static string RelationName(FactionRelationKind relation)
    {
        return Loc.GetString(relation switch
        {
            FactionRelationKind.War => "faction-console-relation-war",
            FactionRelationKind.Alliance => "faction-console-relation-alliance",
            _ => "faction-console-relation-neutral",
        });
    }
}
