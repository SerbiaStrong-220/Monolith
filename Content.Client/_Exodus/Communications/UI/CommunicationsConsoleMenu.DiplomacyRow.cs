using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Communications.UI;

public sealed partial class CommunicationsConsoleMenu
{
    private WarDeclarationRow CreateWarRow(ProtoId<TerritoryFactionPrototype> faction)
    {
        var title = new RichTextLabel { HorizontalExpand = true };
        var badge = new Label();
        var confirmation = new RichTextLabel { Visible = false };
        var cancelButton = new Button { Text = Loc.GetString("faction-console-cancel"), Visible = false };
        var status = new RichTextLabel { HorizontalExpand = true };
        var button = new ConfirmButton
        {
            HorizontalExpand = true,
            ResetTime = TimeSpan.FromSeconds(5),
            CooldownTime = TimeSpan.FromSeconds(0.75),
        };
        button.StyleClasses.Add(StyleNano.StyleClassButtonColorRed);
        button.OnPressed += _ => OnDeclareWar?.Invoke(faction);

        var detail = new RichTextLabel { HorizontalExpand = true };
        var offerButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("war-peace-offer-button"),
        };
        var acceptButton = new ConfirmButton
        {
            HorizontalExpand = true,
            ResetTime = TimeSpan.FromSeconds(5),
            CooldownTime = TimeSpan.FromSeconds(0.75),
            Text = Loc.GetString("war-peace-accept-button"),
        };
        var withdrawButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("war-peace-withdraw-button"),
        };
        var allianceOfferButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("war-alliance-offer-button"),
        };
        allianceOfferButton.StyleClasses.Add(StyleNano.StyleClassButtonColorGreen);
        var allianceAcceptButton = new ConfirmButton
        {
            HorizontalExpand = true,
            ResetTime = TimeSpan.FromSeconds(5),
            CooldownTime = TimeSpan.FromSeconds(0.75),
            Text = Loc.GetString("war-alliance-accept-button"),
        };
        allianceAcceptButton.StyleClasses.Add(StyleNano.StyleClassButtonColorGreen);
        var allianceWithdrawButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("war-alliance-withdraw-button"),
        };
        var breakButton = new ConfirmButton
        {
            HorizontalExpand = true,
            ResetTime = TimeSpan.FromSeconds(5),
            CooldownTime = TimeSpan.FromSeconds(0.75),
            Text = Loc.GetString("war-alliance-break-button"),
        };
        breakButton.StyleClasses.Add(StyleNano.StyleClassButtonColorRed);

        var container = new PanelContainer { StyleClasses = { "BackgroundDark" } };
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(10),
            SeparationOverride = 6,
        };
        container.AddChild(body);
        var header = new BoxContainer { SeparationOverride = 10 };
        header.AddChild(new TextureRect
        {
            Texture = FactionBannerIcon.TryGet(_prototypes, _sprites, faction),
            SetSize = new Vector2(64, 64), Stretch = TextureRect.StretchMode.KeepAspectCentered,
        });
        var identity = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true, VerticalAlignment = VAlignment.Center, SeparationOverride = 6,
        };
        identity.AddChild(title);
        identity.AddChild(badge);
        header.AddChild(identity);
        body.AddChild(header);
        body.AddChild(status);
        body.AddChild(detail);
        body.AddChild(confirmation);
        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        body.AddChild(actions);
        actions.AddChild(button);
        actions.AddChild(offerButton);
        actions.AddChild(acceptButton);
        actions.AddChild(withdrawButton);
        actions.AddChild(allianceOfferButton);
        actions.AddChild(allianceAcceptButton);
        actions.AddChild(allianceWithdrawButton);
        actions.AddChild(breakButton);

        actions.AddChild(cancelButton);
        foreach (var control in actions.Children)
        {
            if (control is not Button action)
                continue;

            action.MinHeight = 32;
            action.Label.ClipText = true;
            if (action is ConfirmButton confirm)
                confirm.ConfirmationText = Loc.GetString("faction-console-confirm");
        }

        var row = new WarDeclarationRow(
            container,
            title,
            badge,
            confirmation,
            cancelButton,
            status,
            detail,
            button,
            offerButton,
            acceptButton,
            withdrawButton,
            allianceOfferButton,
            allianceAcceptButton,
            allianceWithdrawButton,
            breakButton);
        offerButton.OnPressed += _ => OnOfferPeace?.Invoke(faction);
        acceptButton.OnPressed += _ => OnAcceptPeace?.Invoke(faction, row.Target.PeaceOfferId);
        withdrawButton.OnPressed += _ => OnWithdrawPeace?.Invoke(faction, row.Target.PeaceOfferId);
        allianceOfferButton.OnPressed += _ => OnOfferAlliance?.Invoke(faction);
        allianceAcceptButton.OnPressed += _ => OnAcceptAlliance?.Invoke(faction, row.Target.AllianceOfferId);
        allianceWithdrawButton.OnPressed += _ => OnWithdrawAlliance?.Invoke(faction, row.Target.AllianceOfferId);
        breakButton.OnPressed += _ => OnBreakAlliance?.Invoke(faction);
        foreach (var confirm in row.ConfirmationButtons)
        {
            ((BaseButton)confirm).OnPressed += _ =>
            {
                ResetWarConfirmations(confirm.IsConfirming ? confirm : null);
            };
        }

        cancelButton.OnPressed += _ =>
        {
            ResetRowConfirmations(row);
            _lastWarCountdownSecond = long.MinValue;
            UpdateWarAvailability();
            UpdateWarConfirmationAppearance();
        };
        return row;
    }

    private sealed class WarDeclarationRow(
        PanelContainer container,
        RichTextLabel title,
        Label badge,
        RichTextLabel confirmation,
        Button cancelButton,
        RichTextLabel status,
        RichTextLabel detail,
        ConfirmButton button,
        Button offerButton,
        ConfirmButton acceptButton,
        Button withdrawButton,
        Button allianceOfferButton,
        ConfirmButton allianceAcceptButton,
        Button allianceWithdrawButton,
        ConfirmButton breakButton)
    {
        public PanelContainer Container { get; } = container;
        public RichTextLabel Title { get; } = title;
        public Label Badge { get; } = badge;
        public RichTextLabel Confirmation { get; } = confirmation;
        public Button CancelButton { get; } = cancelButton;
        public ConfirmButton? Confirming { get; set; }
        public ConfirmButton[] ConfirmationButtons { get; } = [button, acceptButton, allianceAcceptButton, breakButton];
        public RichTextLabel Status { get; } = status;
        public RichTextLabel Detail { get; } = detail;
        public ConfirmButton Button { get; } = button;
        public Button OfferButton { get; } = offerButton;
        public ConfirmButton AcceptButton { get; } = acceptButton;
        public Button WithdrawButton { get; } = withdrawButton;
        public Button AllianceOfferButton { get; } = allianceOfferButton;
        public ConfirmButton AllianceAcceptButton { get; } = allianceAcceptButton;
        public Button AllianceWithdrawButton { get; } = allianceWithdrawButton;
        public ConfirmButton BreakButton { get; } = breakButton;
        public WarDeclarationTargetState Target { get; set; }
        public string TargetName { get; set; } = string.Empty;
    }
}
