using Content.Server._EinsteinEngines.Language;
using Content.Server.EUI;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Humanoid;
using Content.Server.Jobs;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.LifeInsurance;

public sealed class LifeInsuranceClonerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private LifeInsuranceBackupBatterySystem _backup = default!;
    [Dependency] private LifeInsuranceConsoleSystem _console = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private PuddleSystem _puddle = default!;

    public bool IsAvailable(EntityUid uid, LifeInsuranceClonerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        return !comp.Active && !comp.Failing && _backup.IsOperational(uid);
    }

    /// <summary>
    /// Begins the revival process. Only the capsule animation runs during this time; the body is not
    /// spawned until the process succeeds, so a failure mid-way leaves no stray body to clean up.
    /// </summary>
    public bool TryStartRevival(EntityUid uid, HumanoidCharacterProfile profile, EntityUid mindId, NetUserId user, string company, LifeInsuranceClonerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        if (comp.Active || comp.Failing || !_backup.IsOperational(uid))
            return false;

        // Reject an unusable profile up front so the caller doesn't spend an insurance charge for nothing.
        if (!_prototype.HasIndex<SpeciesPrototype>(profile.Species))
            return false;

        comp.Active = true;
        comp.Progress = 0f;
        comp.PendingMind = mindId;
        comp.PendingUser = user;
        comp.PendingProfile = profile;
        comp.PendingCompany = company;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Cloning);

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LifeInsuranceClonerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // A failed batch decays regardless of power before producing the abomination.
            if (comp.Failing)
            {
                RunFailure(uid, comp, frameTime);
                continue;
            }

            if (!comp.Active)
                continue;

            // The backup battery.
            if (!_backup.IsOperational(uid))
            {
                TriggerFailure(uid, comp);
                continue;
            }

            comp.Progress += frameTime;
            if (comp.Progress < comp.RevivalTime)
                continue;

            Finish(uid, comp);
        }
    }

    private void Finish(EntityUid uid, LifeInsuranceClonerComponent comp)
    {
        // The body is only built on success: spawn it, then transfer the waiting mind into it.
        var body = SpawnBody(uid, comp);

        if (body != null && comp.PendingMind is { } mindId && Exists(mindId))
        {
            _mind.TransferTo(mindId, body.Value, ghostCheckOverride: true);

            // Show the "you wake up in the incubator" window to the revived player.
            if (comp.PendingUser is { } user && _player.TryGetSessionById(user, out var session))
                _eui.OpenEui(new LifeInsuranceWakeUpEui(), session);
        }

        ClearPending(comp);
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Idle);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    /// <summary>
    /// Builds the clone body from the pending profile, applying appearance, traits, company and the
    /// mind's job specials. Returns null if the pending profile is missing or invalid.
    /// </summary>
    private EntityUid? SpawnBody(EntityUid uid, LifeInsuranceClonerComponent comp)
    {
        if (comp.PendingProfile is not { } profile || !_prototype.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            return null;

        var mob = Spawn(species.Prototype, _transform.GetMapCoordinates(uid));
        _humanoid.LoadProfile(mob, profile);
        _metaData.SetEntityName(mob, profile.Name);
        EnsureComp<BankAccountComponent>(mob);
        ApplyTraits(mob, profile);

        // Restore company/faction membership so the clone keeps company-gated access (faction uplinks).
        // CompanySystem normally sets this on PlayerSpawnCompleteEvent, which Spawn does not raise.
        var companyComp = EnsureComp<CompanyComponent>(mob);
        companyComp.CompanyName = comp.PendingCompany;
        Dirty(mob, companyComp);

        // Restore job-granted components (faction membership, command staff) and languages, mirroring
        // standard cloning plus role languages.
        if (comp.PendingMind is { } mindId && _jobs.MindTryGetJob(mindId, out var jobProto))
        {
            foreach (var special in jobProto.Special)
            {
                if (special is AddComponentSpecial or AddLanguageSpecial)
                    special.AfterEquip(mob);
            }
        }

        return mob;
    }

    /// <summary>
    /// Aborts an in-progress revival. No body exists yet, so the capsule simply enters its gory failure state.
    /// </summary>
    private void TriggerFailure(EntityUid uid, LifeInsuranceClonerComponent comp)
    {
        ClearPending(comp);
        comp.Failing = true;
        comp.FailProgress = 0f;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Failed);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    private void ClearPending(LifeInsuranceClonerComponent comp)
    {
        comp.Active = false;
        comp.Progress = 0f;
        comp.PendingMind = null;
        comp.PendingUser = null;
        comp.PendingProfile = null;
        comp.PendingCompany = "None";
    }

    private void RunFailure(EntityUid uid, LifeInsuranceClonerComponent comp, float frameTime)
    {
        comp.FailProgress += frameTime;
        if (comp.FailProgress < comp.FailTime)
            return;

        var coords = Transform(uid).Coordinates;
        Spawn(comp.FailMob, coords);

        // The failed revive bursts out blood.
        var blood = new Solution(comp.FailBloodReagent, comp.FailBloodAmount);
        _puddle.TrySpillAt(coords, blood, out _);

        comp.Failing = false;
        comp.FailProgress = 0f;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Idle);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    /// <summary>
    /// Applies the character's profile traits to the clone.
    /// </summary>
    private void ApplyTraits(EntityUid mob, HumanoidCharacterProfile profile)
    {
        foreach (var traitId in profile.TraitPreferences)
        {
            if (!_prototype.TryIndex<TraitPrototype>(traitId, out var trait))
                continue;

            if (_whitelist.IsWhitelistFail(trait.Whitelist, mob) || _whitelist.IsBlacklistPass(trait.Blacklist, mob))
                continue;

            EntityManager.AddComponents(mob, trait.Components, false);

            if (trait.RemoveLanguagesSpoken is not null)
                foreach (var lang in trait.RemoveLanguagesSpoken)
                    _language.RemoveLanguage(mob, lang, true, false);

            if (trait.RemoveLanguagesUnderstood is not null)
                foreach (var lang in trait.RemoveLanguagesUnderstood)
                    _language.RemoveLanguage(mob, lang, false, true);

            if (trait.LanguagesSpoken is not null)
                foreach (var lang in trait.LanguagesSpoken)
                    _language.AddLanguage(mob, lang, true, false);

            if (trait.LanguagesUnderstood is not null)
                foreach (var lang in trait.LanguagesUnderstood)
                    _language.AddLanguage(mob, lang, false, true);

            if (trait.TraitGear == null)
                continue;

            if (!TryComp<HandsComponent>(mob, out var hands))
                continue;

            var coords = Transform(mob).Coordinates;
            var item = Spawn(trait.TraitGear, coords);
            _hands.TryPickup(mob, item, checkActionBlocker: false, handsComp: hands);
        }
    }
}
