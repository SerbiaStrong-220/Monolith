using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.War;

namespace Content.Client._Exodus.Communications.UI;

public sealed partial class CommunicationsConsoleMenu
{
    public void UpdateWarState(WarDeclarationConsoleState? state)
    {
        _hasWarState = state != null;
        var previous = _warState;
        _warState = state;
        SetFactionLayout(_hasWarState);
        _lastWarCountdownSecond = long.MinValue;

        if (state == null)
        {
            ClearWarRows();
            return;
        }

        if (previous != null && previous.SourceFaction != state.SourceFaction)
            ClearWarRows();

        FactionDashboard.SourceName.Text = Loc.GetString(state.SourceName);
        FactionDashboard.SourceName.ToolTip = FactionDashboard.SourceName.Text;
        FactionDashboard.SourceBanner.Texture = FactionBannerIcon.TryGet(_prototypes, _sprites, state.SourceFaction);
        _warCodeAllowsWar = state.CodeAllowsWar;
        _warSourceName = Loc.GetString(state.SourceName);
        _warRoundRunning = state.RoundRunning;
        _allianceBreakCooldown = state.AllianceBreakCooldown;
        _seenWarTargets.Clear();

        var incomingOffers = 0;
        foreach (var target in state.Targets)
        {
            if (!_seenWarTargets.Add(target.Faction))
                continue;

            if (!_warRows.TryGetValue(target.Faction, out var row))
            {
                row = CreateWarRow(target.Faction);
                _warRows.Add(target.Faction, row);
                FactionDashboard.WarRows.AddChild(row.Container);
            }

            UpdateWarRow(row, target);
            if (target.PeaceDirection == PeaceOfferDirection.Incoming)
                incomingOffers++;
            if (target.AllianceDirection == AllianceOfferDirection.Incoming)
                incomingOffers++;
        }

        FactionDashboard.WarHeading.Text = Loc.GetString("war-declaration-console-title");

        FactionDashboard.Tabs.SetTabTitle(0, incomingOffers == 0
            ? Loc.GetString("faction-console-tab-diplomacy")
            : Loc.GetString("faction-console-tab-incoming", ("count", incomingOffers)));

        _staleWarTargets.Clear();
        foreach (var faction in _warRows.Keys)
        {
            if (!_seenWarTargets.Contains(faction))
                _staleWarTargets.Add(faction);
        }

        foreach (var faction in _staleWarTargets)
        {
            var row = _warRows[faction];
            row.Container.Orphan();
            _warRows.Remove(faction);
        }

        if (previous == null || !SameEntries(previous.Relations, state.Relations) ||
            !SameEntries(previous.PairRelations, state.PairRelations))
        {
            _relationMap.SetRelations(state.Relations, state.PairRelations);
            RebuildLegend(state);
        }

        if (previous == null || !SameEntries(previous.Corporations, state.Corporations))
            RebuildCorporations(state);
        FactionDashboard.WarEmpty.Visible = _warRows.Count == 0;
        UpdateWarAvailability();
        UpdateWarConfirmationAppearance();
        UpdateAnnouncementAvailability();
    }

    private void UpdateWarRow(WarDeclarationRow row, WarDeclarationTargetState target)
    {
        var targetName = Loc.GetString(target.Name);
        if (row.Target.Direction != target.Direction)
            ResetConfirmation(row.Button);

        if (row.Target.PeaceOfferId != target.PeaceOfferId || row.Target.PeaceDirection != target.PeaceDirection)
            ResetConfirmation(row.AcceptButton);

        if (row.Target.AllianceOfferId != target.AllianceOfferId || row.Target.AllianceDirection != target.AllianceDirection)
            ResetConfirmation(row.AllianceAcceptButton);

        if (row.Target.Relation != target.Relation)
            ResetConfirmation(row.BreakButton);

        row.Title.SetMessage(targetName);
        row.Badge.Text = RelationName(target.Relation);
        row.Badge.FontColorOverride = RelationColor(target.Relation);
        row.TargetName = targetName;
        row.Target = target;
        row.AcceptButton.ToolTip = Loc.GetString("war-peace-accept-confirm", ("target", targetName));
        row.AllianceAcceptButton.ToolTip = Loc.GetString("war-alliance-accept-confirm", ("target", targetName));
        row.BreakButton.ToolTip = Loc.GetString(
            "war-alliance-break-confirm",
            ("target", targetName),
            ("minutes", Math.Max(1, (int)_allianceBreakCooldown.TotalMinutes)));
        row.Button.ToolTip = Loc.GetString("war-declaration-console-confirm", ("target", targetName));

        var atWar = target.Direction != WarDeclarationDirection.None;
        var allied = target.Relation == FactionRelationKind.Alliance;
        row.OfferButton.Visible = atWar && target.PeaceDirection == PeaceOfferDirection.None;
        row.AcceptButton.Visible = atWar && target.PeaceDirection == PeaceOfferDirection.Incoming;
        row.WithdrawButton.Visible = atWar && target.PeaceDirection == PeaceOfferDirection.Outgoing;
        row.AllianceOfferButton.Visible = !atWar && !allied && target.AllianceDirection == AllianceOfferDirection.None;
        row.AllianceAcceptButton.Visible = !atWar && target.AllianceDirection == AllianceOfferDirection.Incoming;
        row.AllianceWithdrawButton.Visible = !atWar && target.AllianceDirection == AllianceOfferDirection.Outgoing;
        row.BreakButton.Visible = allied;

        row.Status.Visible = atWar;
        if (atWar)
        {
            var source = target.Direction == WarDeclarationDirection.Outgoing ? _warSourceName : targetName;
            var destination = target.Direction == WarDeclarationDirection.Outgoing ? targetName : _warSourceName;
            row.Status.SetMessage(Loc.GetString(
                "war-declaration-console-status-declared",
                ("source", source),
                ("target", destination)));
            row.Button.Visible = false;
            return;
        }

        row.Button.Visible = true;
    }

    private void UpdateWarAvailability()
    {
        var now = _timing.CurTime;
        var currentSecond = (long)now.TotalSeconds;
        if (currentSecond == _lastWarCountdownSecond)
            return;

        _lastWarCountdownSecond = currentSecond;
        UpdateCodeReadout(now);
        if (!_hasWarState)
            return;

        if (!_warRoundRunning)
        {
            FactionDashboard.WarUnlockLabel.SetMessage(Loc.GetString("war-declaration-round-not-running"));
        }
        else if (_warCodeAllowsWar)
        {
            FactionDashboard.WarUnlockLabel.SetMessage(Loc.GetString("faction-console-diplomacy-ready"));
        }
        else
        {
            FactionDashboard.WarUnlockLabel.SetMessage(Loc.GetString("war-declaration-code-restricted"));
        }

        foreach (var row in _warRows.Values)
            UpdateWarRowAvailability(row, now);

        var factionSelectable = _factionAlerts != null &&
                                _factionAlerts.PendingLevel == null &&
                                _warRoundRunning &&
                                now >= _factionAlerts.AvailableAt;
        FactionAlertLevelButton.Disabled = !factionSelectable;
    }

    private void UpdateWarRowAvailability(WarDeclarationRow row, TimeSpan now)
    {
        var allied = row.Target.Relation == FactionRelationKind.Alliance;
        if (row.Button.Visible && !row.Button.IsConfirming)
        {
            if (allied)
            {
                row.Button.Text = Loc.GetString("faction-console-war-allied");
            }
            else if (!_warCodeAllowsWar)
            {
                row.Button.Text = Loc.GetString("faction-console-war-code-locked");
            }
            else if (_warRoundRunning && now < row.Target.DeclarationAvailableAt)
            {
                row.Button.Text = Loc.GetString(
                    "faction-console-war-locked",
                    ("time", FormatRemaining(row.Target.DeclarationAvailableAt - now)));
            }
            else
            {
                row.Button.Text = Loc.GetString("faction-console-war-button");
            }
        }

        SetConfirmationAvailable(row.Button,
            _warRoundRunning && _warCodeAllowsWar && row.Button.Visible && !allied && now >= row.Target.DeclarationAvailableAt);
        SetConfirmationAvailable(row.AcceptButton, _warRoundRunning && row.AcceptButton.Visible);
        SetConfirmationAvailable(row.AllianceAcceptButton, _warRoundRunning && row.AllianceAcceptButton.Visible);
        SetConfirmationAvailable(row.BreakButton, _warRoundRunning && row.BreakButton.Visible);
        row.OfferButton.Disabled = !_warRoundRunning || now < row.Target.PeaceOfferAvailableAt;
        row.WithdrawButton.Disabled = !_warRoundRunning;
        row.AllianceOfferButton.Disabled = !_warRoundRunning || now < row.Target.AllianceOfferAvailableAt;
        row.AllianceWithdrawButton.Disabled = !_warRoundRunning;
        row.Detail.Visible = true;

        if (row.Target.Direction != WarDeclarationDirection.None)
        {
            switch (row.Target.PeaceDirection)
            {
                case PeaceOfferDirection.Outgoing:
                    row.Detail.SetMessage(Loc.GetString("war-peace-status-outgoing", ("target", row.TargetName)), Color.Gold);
                    break;
                case PeaceOfferDirection.Incoming:
                    row.Detail.SetMessage(Loc.GetString("war-peace-status-incoming", ("target", row.TargetName)), Color.Gold);
                    break;
                default:
                    if (now < row.Target.PeaceOfferAvailableAt)
                    {
                        row.Detail.SetMessage(Loc.GetString("war-peace-offer-cooldown",
                            ("time", FormatRemaining(row.Target.PeaceOfferAvailableAt - now))), Color.Gold);
                    }
                    else
                    {
                        row.Detail.Visible = false;
                    }

                    break;
            }

            return;
        }

        if (row.Target.AllianceDirection == AllianceOfferDirection.Outgoing)
        {
            row.Detail.SetMessage(Loc.GetString("war-alliance-status-outgoing", ("target", row.TargetName)), Color.Gold);
            return;
        }

        if (row.Target.AllianceDirection == AllianceOfferDirection.Incoming)
        {
            row.Detail.SetMessage(Loc.GetString("war-alliance-status-incoming", ("target", row.TargetName)), Color.Gold);
            return;
        }

        if (row.Target.Relation == FactionRelationKind.Alliance)
        {
            row.Detail.Visible = false;
            return;
        }

        if (row.Target.LockReason == WarLockReason.AllianceBreak && now < row.Target.LockUntil)
        {
            row.Detail.SetMessage(Loc.GetString("war-declaration-alliance-break-cooldown",
                ("time", FormatRemaining(row.Target.LockUntil - now))), Color.Gold);
            return;
        }

        if (now < row.Target.DeclarationAvailableAt)
        {
            row.Detail.SetMessage(Loc.GetString("war-declaration-post-war-cooldown",
                ("time", FormatRemaining(row.Target.DeclarationAvailableAt - now))), Color.Gold);
            return;
        }

        if (!allied && row.AllianceOfferButton.Visible && now < row.Target.AllianceOfferAvailableAt)
        {
            row.Detail.SetMessage(Loc.GetString("war-alliance-offer-cooldown",
                ("time", FormatRemaining(row.Target.AllianceOfferAvailableAt - now))), Color.Gold);
            return;
        }

        row.Detail.Visible = false;
    }

    private static void ResetConfirmation(ConfirmButton button)
    {
        button.IsConfirming = false;
        button.Label.Text = button.Text;
        button.Disabled = true;
    }

    private static void SetConfirmationAvailable(ConfirmButton button, bool available)
    {
        if (!available)
            ResetConfirmation(button);
        else if (!button.IsConfirming)
            button.Disabled = false;
    }

    private void ClearWarRows()
    {
        foreach (var row in _warRows.Values)
            row.Container.Orphan();

        _warRows.Clear();
        _seenWarTargets.Clear();
        _staleWarTargets.Clear();
        FactionDashboard.WarScroll.VScroll = 0;
    }

    private void ResetWarConfirmations(ConfirmButton? except = null)
    {
        foreach (var row in _warRows.Values)
            ResetRowConfirmations(row, except);

        _lastWarCountdownSecond = long.MinValue;
        UpdateWarAvailability();
        UpdateWarConfirmationAppearance();
    }

    private static void ResetRowConfirmations(WarDeclarationRow row, ConfirmButton? except = null)
    {
        foreach (var button in row.ConfirmationButtons)
        {
            if (button != except)
                ResetConfirmation(button);
        }
    }

    private void UpdateWarConfirmationAppearance()
    {
        foreach (var row in _warRows.Values)
        {
            UpdateAllianceOfferPulse(row.AllianceAcceptButton);
            var confirming = row.Button.IsConfirming ? row.Button
                : row.AcceptButton.IsConfirming ? row.AcceptButton
                : row.AllianceAcceptButton.IsConfirming ? row.AllianceAcceptButton
                : row.BreakButton.IsConfirming ? row.BreakButton : null;
            if (row.Confirming == confirming)
                continue;

            row.Confirming = confirming;
            row.Confirmation.Visible = confirming != null;
            row.CancelButton.Visible = confirming != null;
            if (confirming != null)
                row.Confirmation.SetMessage(confirming.ToolTip ?? string.Empty, Color.Gold);
        }
    }

    private void UpdateAllianceOfferPulse(ConfirmButton button)
    {
        button.ModulateSelfOverride = null;
        if (!_factionLayout || FactionDashboard.Tabs.CurrentTab != 0 ||
            !button.Visible || button.Disabled || button.IsConfirming)
        {
            return;
        }

        // Tint only the background, preserving the label and the stylesheet's hover color.
        const double periodSeconds = 2.4;
        var phase = (float)(_timing.RealTime.TotalSeconds % periodSeconds / periodSeconds);
        var strength = 0.12f * (1f - MathF.Cos(phase * MathF.Tau));
        button.ModulateSelfOverride = Color.InterpolateBetween(button.ActualModulateSelf, Color.White, strength);
    }
}
