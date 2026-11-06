using Content.Shared._Exodus.MedicalAlerts;
using Content.Shared.CartridgeLoader;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

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
        var state = new MedicalAlertListUiState(GetAlertData(), ent.Comp.NotificationsEnabled);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private void BroadcastAlertToCartridges(MedicalAlertEntry entry)
    {
        var header = Loc.GetString("med-alert-notification-header");
        var msg = Loc.GetString("med-alert-notification",
            ("type", entry.AlertType),
            ("user", entry.SubjectName),
            ("specie", ResolveSpeciesName(entry.SpeciesId) ?? "null"),
            ("grid", entry.GridName ?? Loc.GetString("med-alert-ui-unknown-grid")),
            ("position", $"({entry.Position.X}, {entry.Position.Y})"));

        var entries = GetAlertData();

        var query = EntityQueryEnumerator<MedAlertCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var cartUid, out var cartComp, out var cartridgeComp))
        {
            if (cartridgeComp.LoaderUid is not { } loaderUid || !_cartridgeLoaderQuery.TryComp(loaderUid, out var loaderComp))
                continue;

            if (loaderComp.ActiveProgram == cartUid)
            {
                var state = new MedicalAlertListUiState(entries, cartComp.NotificationsEnabled);
                _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state, loader: loaderComp);
            }

            if (!cartComp.NotificationsEnabled)
                continue;

            _cartridgeLoader.SendNotification(loaderUid, header, msg, loaderComp);
        }
    }

    private string? ResolveSpeciesName(ProtoId<SpeciesPrototype>? speciesId)
    {
        if (speciesId is not { } speciesProtoId || !_prototypeManager.TryIndex(speciesProtoId, out var species))
            return null;

        return Loc.GetString(species.Name);
    }
}
