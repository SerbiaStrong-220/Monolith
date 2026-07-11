using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.Traits.Assorted;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.UserInterface;
using Content.Server.Preferences.Managers;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

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
    [Dependency] private LifeInsuranceClonerSystem _cloner = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

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

    private void OnMapInit(Entity<LifeInsuranceConsoleComponent> ent, ref MapInitEvent args)
    {
        EnsureLinks(ent, ent.Comp);
    }

    private void OnUiOpen(Entity<LifeInsuranceConsoleComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUi(ent, ent.Comp);
    }

    private void OnRecordDna(Entity<LifeInsuranceConsoleComponent> ent, ref LifeInsuranceRecordDnaMessage args)
    {
        if (!_backup.IsOperational(ent))
            return;

        EnsureLinks(ent, ent.Comp);

        if (ent.Comp.Scanner is not { } scannerUid || !TryComp<LifeInsuranceScannerComponent>(scannerUid, out var scanner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-scanner"), ent, args.Actor);
            return;
        }

        if (scanner.BodyContainer.ContainedEntity is not { } body)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-scanner-empty"), ent, args.Actor);
            return;
        }

        TryRecordDna(ent, body, ent.Comp, args.Actor);
    }

    /// <summary>
    /// Stores the DNA of the given body into this console's registry.
    /// </summary>
    private bool TryRecordDna(EntityUid consoleUid, EntityUid body, LifeInsuranceConsoleComponent comp, EntityUid actor)
    {
        if (!_backup.IsOperational(consoleUid))
            return false;

        // No synth
        if (HasComp<SiliconComponent>(body))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-not-organic"), consoleUid, actor);
            return false;
        }

        // Respect the uncloneable trait
        if (HasComp<UncloneableComponent>(body))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-uncloneable"), consoleUid, actor);
            return false;
        }

        // Only playable races can be registered.
        if (!TryComp<HumanoidAppearanceComponent>(body, out var humanoid) ||
            !_prototype.TryIndex(humanoid.Species, out var species) ||
            !species.RoundStart)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-incompatible-dna"), consoleUid, actor);
            return false;
        }

        // Body would record the possesing borer's profile, not the host's.
        if (TryComp<CorticalBorerInfestedComponent>(body, out var infested) && infested.ControlTimeEnd != null)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-foreign-signature"), consoleUid, actor);
            return false;
        }

        if (!_playerManager.TryGetSessionByEntity(body, out var session) ||
            !_prefsManager.TryGetCachedPreferences(session.UserId, out var prefs) ||
            prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-dna"), consoleUid, actor);
            return false;
        }

        // A person is only enrolled once; re-scanning a known client reports their status instead.
        if (comp.Records.ContainsKey(session.UserId))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-already-registered"), consoleUid, actor);
            return false;
        }

        // Let it be like this for now. Registry holds a true snapshot, independent of later edits.
        var snapshot = profile.Clone();

        // Clone can keep company-gated access (faction uplinks).
        var company = TryComp<CompanyComponent>(body, out var companyComp)
            ? companyComp.CompanyName
            : new ProtoId<CompanyPrototype>("None");

        comp.Records[session.UserId] = new LifeInsuranceRecord(snapshot, 0) { Company = company };

        _popup.PopupEntity(Loc.GetString("life-insurance-dna-recorded", ("name", profile.Name)), consoleUid, actor);
        UpdateUi(consoleUid, comp);
        return true;
    }

    private void OnBuy(Entity<LifeInsuranceConsoleComponent> ent, ref LifeInsuranceBuyMessage args)
    {
        if (!_backup.IsOperational(ent))
            return;

        var userId = new NetUserId(args.UserId);

        if (!ent.Comp.Records.TryGetValue(userId, out var record))
            return;

        // Only reg a person who is currently alive.
        if (!IsTargetAlive(userId))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-target-not-alive"), ent, args.Actor);
            return;
        }

        if (record.Insurances >= ent.Comp.MaxInsurances)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-max-reached"), ent, args.Actor);
            return;
        }

        // Don't sell unless cloning capsule still connected.
        EnsureLinks(ent, ent.Comp);
        if (ent.Comp.Cloner is not { } cloner || !Exists(cloner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), ent, args.Actor);
            return;
        }

        var price = _cfg.GetCVar(EXCVars.LifeInsurancePrice);
        if (!_bank.TryBankWithdraw(args.Actor, price))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-insufficient-funds"), ent, args.Actor);
            return;
        }

        record.Insurances++;
        _popup.PopupEntity(Loc.GetString("life-insurance-purchased", ("name", record.Profile.Name)), ent, args.Actor);
        UpdateUi(ent, ent.Comp);
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

    private void OnDelete(Entity<LifeInsuranceConsoleComponent> ent, ref LifeInsuranceDeleteMessage args)
    {
        if (!_backup.IsOperational(ent))
            return;

        // Only frac leaders may purge paid policies.
        var tags = _access.FindAccessTags(args.Actor);
        if (!ent.Comp.DeleteAccess.Any(req => tags.Contains(req)))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-access"), ent, args.Actor);
            return;
        }

        ent.Comp.Records.Remove(new NetUserId(args.UserId));
        UpdateUi(ent, ent.Comp);
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

        // Filter by component via broadphase instead of scanning every entity in range.
        if (comp.Scanner == null)
        {
            foreach (var (ent, scanner) in _lookup.GetEntitiesInRange<LifeInsuranceScannerComponent>(coords, comp.LinkRange))
            {
                comp.Scanner = ent;
                scanner.ConnectedConsole = uid;
                break;
            }
        }

        if (comp.Cloner == null)
        {
            foreach (var (ent, cloner) in _lookup.GetEntitiesInRange<LifeInsuranceClonerComponent>(coords, comp.LinkRange))
            {
                comp.Cloner = ent;
                cloner.ConnectedConsole = uid;
                break;
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
                Name = kv.Value.Profile.Name,
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
            _cfg.GetCVar(EXCVars.LifeInsurancePrice));

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
        (EntityUid Console, LifeInsuranceConsoleComponent Comp, LifeInsuranceRecord Record)? fallback = null;

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
            if (comp.Cloner is { } cloner && _cloner.IsAvailable(cloner))
            {
                console = uid;
                consoleComp = comp;
                record = found;
                return true;
            }

            // Otherwise remember it as a fallback so the player at least gets a clear reason.
            fallback = (uid, comp, found);
        }

        if (fallback is { } fb)
        {
            console = fb.Console;
            consoleComp = fb.Comp;
            record = fb.Record;
            return true;
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
