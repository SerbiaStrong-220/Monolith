using Content.Shared._Exodus.MedicalAlerts;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Exodus.MedicalAlerts;

public sealed partial class MedicalAlertSystem
{
    private void InitializeUi()
    {
        SubscribeLocalEvent<MedAlertCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<MedAlertCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnUiReady(Entity<MedAlertCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateCartridgeUi(ent, args.Loader);
    }

    private void OnUiMessage(Entity<MedAlertCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not MedicalAlertCommandMessageEvent command)
            return;

        switch (command.Command)
        {
            case MedicalAlertCommand.RefreshList:
                UpdateCartridgeUi(ent, GetEntity(args.LoaderUid));
                break;
            case MedicalAlertCommand.ToggleNotifications:
                ent.Comp.NotificationsEnabled = !ent.Comp.NotificationsEnabled;
                UpdateCartridgeUi(ent, GetEntity(args.LoaderUid));
                break;
        }
    }

    private void UpdateCartridgeUi(Entity<MedAlertCartridgeComponent> ent, EntityUid loaderUid)
    {
        var state = new MedicalAlertListUiState(GetEntries(), ent.Comp.NotificationsEnabled);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
