using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._Exodus.Communications;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
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

        _menu = this.CreateWindow<CommunicationsConsoleMenu>();
        _menu.OnAnnounce += AnnounceButtonPressed;
        _menu.OnBroadcast += BroadcastButtonPressed;
        _menu.OnAlertLevel += AlertLevelSelected;
        _menu.OnDeclareWar += DeclareWar;
        _menu.OnOfferPeace += OfferPeace;
        _menu.OnAcceptPeace += AcceptPeace;
        _menu.OnWithdrawPeace += WithdrawPeace;
    }

    public void AlertLevelSelected(string level)
    {
        if (_menu!.AlertLevelSelectable)
        {
            _menu.CurrentLevel = level;
            SendMessage(new CommunicationsConsoleSelectAlertLevelMessage(level));
        }
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

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CommunicationsConsoleInterfaceState commsState)
            return;

        if (_menu != null)
        {
            _menu.CanAnnounce = commsState.CanAnnounce;
            _menu.CanBroadcast = commsState.CanBroadcast;
            _menu.AlertLevelSelectable = commsState.AlertLevels != null && !float.IsNaN(commsState.CurrentAlertDelay) && commsState.CurrentAlertDelay <= 0;
            _menu.CurrentLevel = commsState.CurrentAlert;

            _menu.UpdateAlertLevels(commsState.AlertLevels, _menu.CurrentLevel);
            _menu.UpdateWarState(commsState.WarState);
            _menu.AlertLevelButton.Disabled = !_menu.AlertLevelSelectable;
            _menu.AnnounceButton.Disabled = !_menu.CanAnnounce;
            _menu.BroadcastButton.Disabled = !_menu.CanBroadcast;
        }
    }
}
