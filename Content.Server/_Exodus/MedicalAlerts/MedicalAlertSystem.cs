using System.Diagnostics.CodeAnalysis;
using Content.Server.CartridgeLoader;
using Content.Server._NF.SectorServices;
using Content.Shared._Exodus.MedicalAlerts;
using Content.Shared.CartridgeLoader;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.MedicalAlerts;

public sealed partial class MedicalAlertSystem : SharedMedicalAlertSystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly SectorServiceSystem _sectorService = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedicalAlertRaisedEvent>(OnMedicalAlertRaised);
        InitializeUi();
    }

    private void OnMedicalAlertRaised(ref MedicalAlertRaisedEvent args)
    {
        if (!TryGetAlertData(out var data))
            return;

        var alertType = GetAlertType(args.CurrentState, args.PreviousState);
        if (alertType == null)
            return;

        data.LastEntryId++;
        var subjectName = Identity.Name(args.Subject, EntityManager);
        var entry = new MedicalAlertEntry(
            data.LastEntryId,
            alertType.Value,
            subjectName,
            args.SpeciesName,
            args.GridName,
            args.PositionX,
            args.PositionY,
            _timing.CurTime);

        data.Entries.Add(entry);
        TrimEntries(data);

        BroadcastAlert(entry);
    }

    private static MedicalAlertType? GetAlertType(MobState current, MobState previous)
    {
        return current switch
        {
            MobState.Dead => MedicalAlertType.Dead,
            MobState.Critical when previous == MobState.Dead => MedicalAlertType.Revived,
            MobState.Alive when previous == MobState.Dead => MedicalAlertType.Revived,
            MobState.Critical => MedicalAlertType.Critical,
            _ => null,
        };
    }

    private void TrimEntries(MedicalAlertDataComponent data)
    {
        var overflow = data.Entries.Count - MaxEntries;
        if (overflow <= 0)
            return;

        data.Entries.RemoveRange(0, overflow);
    }

    private void BroadcastAlert(MedicalAlertEntry entry)
    {
        var header = Loc.GetString("med-alert-notification-header");
        var msg = Loc.GetString(GetNotificationLocId(entry.AlertType),
            ("user", entry.SubjectName),
            ("specie", entry.SpeciesName),
            ("grid", entry.GridName),
            ("position", $"({entry.PositionX}, {entry.PositionY})"));

        var entries = GetEntries();

        var loaders = EntityQueryEnumerator<CartridgeLoaderComponent>();
        while (loaders.MoveNext(out var loaderUid, out var loaderComp))
        {
            if (!_cartridgeLoader.TryGetProgram<MedAlertCartridgeComponent>(loaderUid, out _, out var cartComp, true, loaderComp))
                continue;

            var state = new MedicalAlertListUiState(entries, cartComp.NotificationsEnabled);
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state, loader: loaderComp);

            if (!cartComp.NotificationsEnabled)
                continue;

            _cartridgeLoader.SendNotification(loaderUid, header, msg, loaderComp);
        }
    }

    private static LocId GetNotificationLocId(MedicalAlertType type)
    {
        return type switch
        {
            MedicalAlertType.Dead => "med-alert-notification-dead",
            MedicalAlertType.Critical => "med-alert-notification-critical",
            MedicalAlertType.Revived => "med-alert-notification-revived",
            _ => "med-alert-notification-critical",
        };
    }

    public bool TryGetAlertData([NotNullWhen(true)] out MedicalAlertDataComponent? data)
    {
        return TryComp(_sectorService.GetServiceEntity(), out data);
    }

    public IReadOnlyList<MedicalAlertEntry> GetEntries()
    {
        if (!TryGetAlertData(out var data))
            return Array.Empty<MedicalAlertEntry>();

        return data.Entries;
    }
}
