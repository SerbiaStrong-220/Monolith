using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Popups;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.Preferences;
using Content.Shared.UserInterface;
using Content.Server.Preferences.Managers;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._Exodus.LifeInsurance;

/// <summary>
/// Life insurance console. Records player DNA from the linked scanner, sells insurance charges,
/// and reports machine status. Auto-links to nearby scanner/cloner capsules (machines are static).
/// </summary>
public sealed class LifeInsuranceConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefsManager = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly LifeInsuranceBackupBatterySystem _backup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceRecordDnaMessage>(OnRecordDna);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceBuyMessage>(OnBuy);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceDeleteMessage>(OnDelete);
    }

    private void OnMapInit(EntityUid uid, LifeInsuranceConsoleComponent comp, MapInitEvent args)
    {
        EnsureLinks(uid, comp);
    }

    private void OnUiOpen(EntityUid uid, LifeInsuranceConsoleComponent comp, AfterActivatableUIOpenEvent args)
    {
        UpdateUi(uid, comp);
    }

    private void OnRecordDna(EntityUid uid, LifeInsuranceConsoleComponent comp, LifeInsuranceRecordDnaMessage args)
    {
        if (!_backup.IsOperational(uid))
            return;

        EnsureLinks(uid, comp);

        if (comp.Scanner is not { } scannerUid || !TryComp<LifeInsuranceScannerComponent>(scannerUid, out var scanner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-scanner"), uid, args.Actor);
            return;
        }

        if (scanner.BodyContainer.ContainedEntity is not { } body)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-scanner-empty"), uid, args.Actor);
            return;
        }

        TryRecordDna(uid, body, comp, args.Actor);
    }

    /// <summary>
    /// Stores the DNA (character profile) of the given body into this console's registry.
    /// </summary>
    public bool TryRecordDna(EntityUid consoleUid, EntityUid body, LifeInsuranceConsoleComponent? comp = null, EntityUid? actor = null)
    {
        if (!Resolve(consoleUid, ref comp) || !_backup.IsOperational(consoleUid))
            return false;

        if (!_playerManager.TryGetSessionByEntity(body, out var session) ||
            !_prefsManager.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
        {
            if (actor != null)
                _popup.PopupEntity(Loc.GetString("life-insurance-no-dna"), consoleUid, actor.Value);
            return false;
        }

        if (comp.Records.TryGetValue(session.UserId, out var existing))
            existing.Profile = profile;
        else
            comp.Records[session.UserId] = new LifeInsuranceRecord(profile.Name, profile, 0);

        _popup.PopupEntity(Loc.GetString("life-insurance-dna-recorded", ("name", profile.Name)), consoleUid, actor ?? body);
        UpdateUi(consoleUid, comp);
        return true;
    }

    /// <summary>
    /// TEMP (single-player testing): records the occupant directly from the scanner so the tester
    /// doesn't have to be inside the capsule and press the console button at the same time.
    /// </summary>
    public bool TryAutoRecordFromScanner(EntityUid scannerUid, EntityUid body)
    {
        var query = EntityQueryEnumerator<LifeInsuranceConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var comp))
        {
            EnsureLinks(consoleUid, comp);
            if (comp.Scanner == scannerUid)
                return TryRecordDna(consoleUid, body, comp);
        }

        return false;
    }

    private void OnBuy(EntityUid uid, LifeInsuranceConsoleComponent comp, LifeInsuranceBuyMessage args)
    {
        if (!_backup.IsOperational(uid))
            return;

        if (!comp.Records.TryGetValue(new NetUserId(args.UserId), out var record))
            return;

        if (record.Insurances >= comp.MaxInsurances)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-max-reached"), uid, args.Actor);
            return;
        }

        var price = _cfg.GetCVar(XCVars.LifeInsurancePrice);
        if (!_bank.TryBankWithdraw(args.Actor, price))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-insufficient-funds"), uid, args.Actor);
            return;
        }

        record.Insurances++;
        _popup.PopupEntity(Loc.GetString("life-insurance-purchased", ("name", record.Name)), uid, args.Actor);
        UpdateUi(uid, comp);
    }

    private void OnDelete(EntityUid uid, LifeInsuranceConsoleComponent comp, LifeInsuranceDeleteMessage args)
    {
        if (!_backup.IsOperational(uid))
            return;

        comp.Records.Remove(new NetUserId(args.UserId));
        UpdateUi(uid, comp);
    }

    /// <summary>
    /// Discovers and links the nearby scanner and cloner capsules if not already linked.
    /// </summary>
    public void EnsureLinks(EntityUid uid, LifeInsuranceConsoleComponent comp)
    {
        if (Exists(comp.Scanner) && Exists(comp.Cloner))
            return;

        var coords = Transform(uid).Coordinates;
        foreach (var ent in _lookup.GetEntitiesInRange(coords, comp.LinkRange))
        {
            if (comp.Scanner == null && TryComp<LifeInsuranceScannerComponent>(ent, out var scanner))
            {
                comp.Scanner = ent;
                scanner.ConnectedConsole = uid;
            }

            if (comp.Cloner == null && TryComp<LifeInsuranceClonerComponent>(ent, out var cloner))
            {
                comp.Cloner = ent;
                cloner.ConnectedConsole = uid;
            }
        }
    }

    public void UpdateUi(EntityUid uid, LifeInsuranceConsoleComponent? comp = null)
    {
        if (!Resolve(uid, ref comp) || !_ui.HasUi(uid, LifeInsuranceConsoleUiKey.Key))
            return;

        if (!_backup.IsOperational(uid))
        {
            _ui.CloseUi(uid, LifeInsuranceConsoleUiKey.Key);
            return;
        }

        EnsureLinks(uid, comp);

        var records = comp.Records
            .Select(kv => new LifeInsuranceRecordEntry
            {
                UserId = kv.Key.UserId,
                Name = kv.Value.Name,
                Insurances = kv.Value.Insurances
            })
            .ToList();

        string? occupantName = null;
        if (comp.Scanner is { } scannerUid &&
            TryComp<LifeInsuranceScannerComponent>(scannerUid, out var scanner) &&
            scanner.BodyContainer.ContainedEntity is { } body)
        {
            occupantName = MetaData(body).EntityName;
        }

        var scannerStatus = comp.Scanner is { } sUid
            ? _backup.GetStatus(sUid, true)
            : new LifeInsuranceMachineStatus();
        var clonerStatus = comp.Cloner is { } cUid
            ? _backup.GetStatus(cUid, true)
            : new LifeInsuranceMachineStatus();

        var state = new LifeInsuranceConsoleState(
            records,
            comp.MaxInsurances,
            occupantName,
            scannerStatus,
            clonerStatus,
            _cfg.GetCVar(XCVars.LifeInsurancePrice));

        _ui.SetUiState(uid, LifeInsuranceConsoleUiKey.Key, state);
    }

    /// <summary>
    /// Finds the console and record holding insurance for the given user, if any has charges left.
    /// </summary>
    public bool TryFindInsurance(NetUserId user,
        out EntityUid console,
        [NotNullWhen(true)] out LifeInsuranceConsoleComponent? consoleComp,
        [NotNullWhen(true)] out LifeInsuranceRecord? record)
    {
        var query = EntityQueryEnumerator<LifeInsuranceConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Records.TryGetValue(user, out var found) && found.Insurances > 0)
            {
                console = uid;
                consoleComp = comp;
                record = found;
                return true;
            }
        }

        console = default;
        consoleComp = null;
        record = null;
        return false;
    }

    /// <summary>
    /// Total insurance charges recorded for the user across all consoles.
    /// </summary>
    public int GetInsuranceCount(NetUserId user)
    {
        var total = 0;
        var query = EntityQueryEnumerator<LifeInsuranceConsoleComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Records.TryGetValue(user, out var found))
                total += found.Insurances;
        }

        return total;
    }
}
