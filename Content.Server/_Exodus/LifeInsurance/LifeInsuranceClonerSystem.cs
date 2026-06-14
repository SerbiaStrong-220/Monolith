using Content.Server._EinsteinEngines.Language;
using Content.Server.EUI;
using Content.Server.Humanoid;
using Content.Server.Jobs;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.LifeInsurance;

/// <summary>
/// Cloning capsule of the life insurance machine. Rebuilds an insured player's body from a recorded
/// character profile and transfers their mind into it once the revival timer completes.
/// </summary>
public sealed class LifeInsuranceClonerSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly LifeInsuranceBackupBatterySystem _backup = default!;
    [Dependency] private readonly LifeInsuranceConsoleSystem _console = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public const string ContainerId = "life-insurance-cloner-body";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceClonerComponent, ComponentInit>(OnInit);
    }

    private void OnInit(EntityUid uid, LifeInsuranceClonerComponent comp, ComponentInit args)
    {
        comp.BodyContainer = _container.EnsureContainer<ContainerSlot>(uid, ContainerId);
    }

    public bool IsAvailable(EntityUid uid, LifeInsuranceClonerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        return !comp.Active && !comp.Failing && _backup.IsOperational(uid);
    }

    /// <summary>
    /// Begins growing a clone from the given profile. The mind is transferred once revival completes.
    /// </summary>
    public bool TryStartRevival(EntityUid uid, HumanoidCharacterProfile profile, EntityUid mindId, NetUserId user, string company, LifeInsuranceClonerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        if (comp.Active || comp.Failing || !_backup.IsOperational(uid))
            return false;

        if (!_prototype.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            return false;

        var mob = Spawn(species.Prototype, _transform.GetMapCoordinates(uid));
        _humanoid.LoadProfile(mob, profile);
        _metaData.SetEntityName(mob, profile.Name);
        EnsureComp<BankAccountComponent>(mob);
        ApplyTraits(mob, profile);

        // Restore company/faction membership so the clone keeps company-gated access (faction uplinks).
        // CompanySystem normally sets this on PlayerSpawnCompleteEvent, which Spawn does not raise.
        var companyComp = EnsureComp<CompanyComponent>(mob);
        companyComp.CompanyName = company;
        Dirty(mob, companyComp);

        // Restore job-granted components (faction membership, command staff) and languages, mirroring
        // standard cloning plus role languages. Implants/loadout (other JobSpecial types) are not applied.
        if (_jobs.MindTryGetJob(mindId, out var jobProto))
        {
            foreach (var special in jobProto.Special)
            {
                if (special is AddComponentSpecial or AddLanguageSpecial)
                    special.AfterEquip(mob);
            }
        }

        if (!_container.Insert(mob, comp.BodyContainer))
        {
            QueueDel(mob);
            return false;
        }

        comp.Active = true;
        comp.Progress = 0f;
        comp.PendingMind = mindId;
        comp.PendingUser = user;
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

            // The backup battery bridges brief outages. If power is fully gone (battery depleted),
            // the batch is ruined and decays into a botched abomination.
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
        var body = comp.BodyContainer.ContainedEntity;

        if (body != null)
        {
            _container.Remove(body.Value, comp.BodyContainer);

            if (comp.PendingMind is { } mindId && Exists(mindId))
            {
                _mind.TransferTo(mindId, body.Value, ghostCheckOverride: true);

                // Show the narrative "you wake up in the incubator" window to the revived player.
                if (comp.PendingUser is { } user && _player.TryGetSessionById(user, out var session))
                    _eui.OpenEui(new LifeInsuranceWakeUpEui(), session);
            }
        }

        comp.Active = false;
        comp.Progress = 0f;
        comp.PendingMind = null;
        comp.PendingUser = null;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Idle);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    /// <summary>
    /// Aborts an in-progress revival (power fully lost): the half-grown body is destroyed and the
    /// capsule enters a gory failure state. The insurance charge stays spent.
    /// </summary>
    private void TriggerFailure(EntityUid uid, LifeInsuranceClonerComponent comp)
    {
        if (comp.BodyContainer.ContainedEntity is { } body)
            QueueDel(body);

        comp.Active = false;
        comp.Progress = 0f;
        comp.PendingMind = null;
        comp.PendingUser = null;
        comp.Failing = true;
        comp.FailProgress = 0f;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Failed);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    private void RunFailure(EntityUid uid, LifeInsuranceClonerComponent comp, float frameTime)
    {
        comp.FailProgress += frameTime;
        if (comp.FailProgress < comp.FailTime)
            return;

        Spawn(comp.FailMob, _transform.GetMapCoordinates(uid));

        comp.Failing = false;
        comp.FailProgress = 0f;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Idle);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    /// <summary>
    /// Applies the character's profile traits (components, languages, trait gear) to the clone,
    /// mirroring <see cref="Content.Server.Traits.TraitSystem"/> which only runs on normal spawns.
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
