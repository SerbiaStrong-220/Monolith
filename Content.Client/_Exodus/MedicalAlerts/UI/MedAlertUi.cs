using Content.Client.UserInterface.Fragments;
using Content.Shared._Exodus.MedicalAlerts;
using Content.Shared.CartridgeLoader;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.MedicalAlerts.UI;

[UsedImplicitly]
public sealed partial class MedAlertUi : UIFragment
{
    private MedAlertUiFragmentList? _fragment;
    private BoundUserInterface? _userInterface;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MedAlertUiFragmentList();
        _fragment.OnRefreshButtonPressed += OnRefreshPressed;
        _fragment.OnToggleNotificationPressed += OnToggleNotificationPressed;
        _userInterface = userInterface;
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (_fragment == null || state is not MedicalAlertListUiState listState)
            return;

        _fragment.SetAlerts(listState.Entries);
        _fragment.SetNotificationsEnabled(listState.NotificationsEnabled);
    }

    private void OnRefreshPressed()
    {
        SendMessage(new MedicalAlertCommandMessageEvent(MedicalAlertCommand.RefreshList));
    }

    private void OnToggleNotificationPressed()
    {
        SendMessage(new MedicalAlertCommandMessageEvent(MedicalAlertCommand.ToggleNotifications));
    }

    private void SendMessage(CartridgeMessageEvent msg)
    {
        _userInterface?.SendMessage(new CartridgeUiMessage(msg));
    }
}
