using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.Traits.Assorted;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.Company;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.UserInterface;
using Content.Server.Preferences.Managers;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._Exodus.LifeInsurance;

public sealed class LifeInsuranceConsoleSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IServerPreferencesManager _prefsManager = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private LifeInsuranceBackupBatterySystem _backup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceRecordDnaMessage>(OnRecordDna);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceBuyMessage>(OnBuy);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceDeleteMessage>(OnDelete);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
    }

    /// <summary>
    /// When a player returns to the lobby, purge their DNA from every console.
    /// </summary>
    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent args)
    {
        var user = args.PlayerSession.UserId;
        var query = EntityQueryEnumerator<LifeInsuranceConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Records.Remove(user))
                UpdateUi(uid, comp);
        }
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
    /// Stores the DNA of the given body into this console's registry.
    /// </summary>
    public bool TryRecordDna(EntityUid consoleUid, EntityUid body, LifeInsuranceConsoleComponent? comp = null, EntityUid? actor = null)
    {
        if (!Resolve(consoleUid, ref comp) || !_backup.IsOperational(consoleUid))
            return false;

        // No synth
        if (HasComp<SiliconComponent>(body))
        {
            if (actor != null)
                _popup.PopupEntity(Loc.GetString("life-insurance-not-organic"), consoleUid, actor.Value);
            return false;
        }

        // Respect the uncloneable trait
        if (HasComp<UncloneableComponent>(body))
        {
            if (actor != null)
                _popup.PopupEntity(Loc.GetString("life-insurance-uncloneable"), consoleUid, actor.Value);
            return false;
        }

        if (!_playerManager.TryGetSessionByEntity(body, out var session) ||
            !_prefsManager.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
        {
            if (actor != null)
                _popup.PopupEntity(Loc.GetString("life-insurance-no-dna"), consoleUid, actor.Value);
            return false;
        }

        // A person is only enrolled once; re-scanning a known client reports their status instead.
        if (comp.Records.ContainsKey(session.UserId))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-already-registered"), consoleUid, actor ?? body);
            return false;
        }

        // Let it be like this for now. Registry holds a true snapshot, independent of later edits.
        var snapshot = profile.Clone();

        // Clone can keep company-gated access (faction uplinks).
        var company = TryComp<CompanyComponent>(body, out var companyComp) ? companyComp.CompanyName.Id : "None";

        comp.Records[session.UserId] = new LifeInsuranceRecord(snapshot.Name, snapshot, 0) { Company = company };

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

        var userId = new NetUserId(args.UserId);

        if (!comp.Records.TryGetValue(userId, out var record))
            return;

        // Only reg a person who is currently alive.
        if (!IsTargetAlive(userId))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-target-not-alive"), uid, args.Actor);
            return;
        }

        if (record.Insurances >= comp.MaxInsurances)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-max-reached"), uid, args.Actor);
            return;
        }

        // Don't sell unless cloning capsule still connected.
        EnsureLinks(uid, comp);
        if (comp.Cloner is not { } cloner || !Exists(cloner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), uid, args.Actor);
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

    /// <summary>
    /// Whether the recorded person is currently connected and controlling a living body.
    /// </summary>
    private bool IsTargetAlive(NetUserId userId)
    {
        return _playerManager.TryGetSessionById(userId, out var session)
            && session.AttachedEntity is { } ent
            && _mobState.IsAlive(ent);
    }

    private void OnDelete(EntityUid uid, LifeInsuranceConsoleComponent comp, LifeInsuranceDeleteMessage args)
    {
        if (!_backup.IsOperational(uid))
            return;

        // Only frac leaders may purge paid policies.
        var tags = _access.FindAccessTags(args.Actor);
        if (!comp.DeleteAccess.Any(req => tags.Contains(req)))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-access"), uid, args.Actor);
            return;
        }

        comp.Records.Remove(new NetUserId(args.UserId));
        UpdateUi(uid, comp);
    }

    /// <summary>
    /// Discovers and links the nearby scanner and cloner capsules if not already linked.
    /// </summary>
    public void EnsureLinks(EntityUid uid, LifeInsuranceConsoleComponent comp)
    {
        // Drop stale references so a replacement can be picked up.
        if (!Exists(comp.Scanner))
            comp.Scanner = null;
        if (!Exists(comp.Cloner))
            comp.Cloner = null;

        if (comp.Scanner != null && comp.Cloner != null)
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
        EntityUid fallbackConsole = default;
        LifeInsuranceConsoleComponent? fallbackComp = null;
        LifeInsuranceRecord? fallbackRecord = null;

        var query = EntityQueryEnumerator<LifeInsuranceConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // A powered-down or destroyed console can't serve its policy database.
            if (!_backup.IsOperational(uid))
                continue;

            EnsureLinks(uid, comp);

            if (!comp.Records.TryGetValue(user, out var found) || found.Insurances <= 0)
                continue;

            // Prefer a console whose cloning capsule is actually free to use right now.
            if (IsClonerAvailable(comp.Cloner))
            {
                console = uid;
                consoleComp = comp;
                record = found;
                return true;
            }

            // Otherwise remember it as a fallback so the player at least gets a clear reason.
            fallbackConsole = uid;
            fallbackComp = comp;
            fallbackRecord = found;
        }

        if (fallbackComp != null && fallbackRecord != null)
        {
            console = fallbackConsole;
            consoleComp = fallbackComp;
            record = fallbackRecord;
            return true;
        }

        console = default;
        consoleComp = null;
        record = null;
        return false;
    }

    private bool IsClonerAvailable(EntityUid? cloner)
    {
        return cloner is { } c
            && TryComp<LifeInsuranceClonerComponent>(c, out var cl)
            && !cl.Active
            && !cl.Failing
            && _backup.IsOperational(c);
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
