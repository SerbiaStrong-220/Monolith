using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._Exodus.Communications;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Communications.UI;

public sealed partial class CommunicationsConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private IConfigurationManager _cfg = default!;

    [ViewVariables]
    private CommunicationsConsoleMenu? _menu;

    public CommunicationsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        // The first server state selects the layout and size before the window is shown.
        _menu = this.CreateDisposableControl<CommunicationsConsoleMenu>();
        _menu.OnClose += Close;
        _menu.OnAnnounce += AnnounceButtonPressed;
        _menu.OnBroadcast += BroadcastButtonPressed;
        _menu.OnAlertLevel += AlertLevelSelected;
        _menu.OnDeclareWar += DeclareWar;
        _menu.OnOfferPeace += OfferPeace;
        _menu.OnAcceptPeace += AcceptPeace;
        _menu.OnWithdrawPeace += WithdrawPeace;
        _menu.OnFactionAlertLevel += FactionAlertLevelSelected;
        _menu.OnOfferAlliance += OfferAlliance;
        _menu.OnAcceptAlliance += AcceptAlliance;
        _menu.OnWithdrawAlliance += WithdrawAlliance;
        _menu.OnBreakAlliance += BreakAlliance;
    }

    public void AlertLevelSelected(string level)
    {
        if (_menu!.AlertLevelSelectable)
        {
            _menu.CurrentLevel = level;
            SendMessage(new CommunicationsConsoleSelectAlertLevelMessage(level));
        }
    }

    private void FactionAlertLevelSelected(string level)
    {
        SendMessage(new CommunicationsConsoleSelectFactionAlertLevelMessage(level));
    }

    public void AnnounceButtonPressed(string message)
    {
        var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength);
        var msg = SharedChatSystem.SanitizeAnnouncement(message, maxLength);
        SendMessage(new CommunicationsConsoleAnnounceMessage(msg));
    }

    public void BroadcastButtonPressed(string message)
    {
        SendMessage(new CommunicationsConsoleBroadcastMessage(message));
    }

    private void DeclareWar(ProtoId<TerritoryFactionPrototype> targetFaction)
    {
        SendMessage(new CommunicationsConsoleDeclareWarMessage(targetFaction));
    }

    private void OfferPeace(ProtoId<TerritoryFactionPrototype> targetFaction)
    {
        SendMessage(new CommunicationsConsoleOfferPeaceMessage(targetFaction));
    }

    private void AcceptPeace(ProtoId<TerritoryFactionPrototype> targetFaction, int offerId)
    {
        SendMessage(new CommunicationsConsoleAcceptPeaceMessage(targetFaction, offerId));
    }

    private void WithdrawPeace(ProtoId<TerritoryFactionPrototype> targetFaction, int offerId)
    {
        SendMessage(new CommunicationsConsoleWithdrawPeaceMessage(targetFaction, offerId));
    }

    private void OfferAlliance(ProtoId<TerritoryFactionPrototype> targetFaction)
    {
        SendMessage(new CommunicationsConsoleOfferAllianceMessage(targetFaction));
    }

    private void AcceptAlliance(ProtoId<TerritoryFactionPrototype> targetFaction, int offerId)
    {
        SendMessage(new CommunicationsConsoleAcceptAllianceMessage(targetFaction, offerId));
    }

    private void WithdrawAlliance(ProtoId<TerritoryFactionPrototype> targetFaction, int offerId)
    {
        SendMessage(new CommunicationsConsoleWithdrawAllianceMessage(targetFaction, offerId));
    }

    private void BreakAlliance(ProtoId<TerritoryFactionPrototype> targetFaction)
    {
        SendMessage(new CommunicationsConsoleBreakAllianceMessage(targetFaction));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CommunicationsConsoleInterfaceState commsState)
            return;

        if (_menu != null)
        {
            _menu.CanAnnounce = commsState.CanAnnounce;
            _menu.CanBroadcast = commsState.CanBroadcast;
            _menu.AlertLevelSelectable = commsState.AlertLevels != null && !float.IsNaN(commsState.CurrentAlertDelay) && commsState.CurrentAlertDelay <= 0 && commsState.PendingAlert == null;
            _menu.CurrentLevel = commsState.CurrentAlert;

            _menu.UpdateAlertLevels(commsState.AlertLevels, _menu.CurrentLevel);
            _menu.UpdateSectorCode(
                commsState.CurrentAlert,
                commsState.PendingAlert,
                commsState.PendingAlertAt,
                commsState.CurrentAlertColor);
            _menu.UpdateFactionAlerts(commsState.FactionAlertState);
            _menu.UpdateWarState(commsState.WarState);
            _menu.AlertLevelButton.Disabled = !_menu.AlertLevelSelectable;
            _menu.BroadcastButton.Disabled = !_menu.CanBroadcast;
            _menu.UpdateAnnouncementAvailability();

            if (_menu.IsOpen || !IsOpened)
                return;

            EntMan.System<UserInterfaceSystem>().RegisterControl(this, _menu);
            if (UiSystem.TryGetPosition(Owner, UiKey, out var position))
                _menu.Open(position);
            else
                _menu.OpenCentered();
        }
    }
}
