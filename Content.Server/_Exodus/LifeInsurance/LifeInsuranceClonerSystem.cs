using Content.Server.EUI;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Humanoid;
using Content.Server.Jobs;
using Content.Server.Traits;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
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
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private TraitSystem _traits = default!;
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
        comp.Progress = TimeSpan.Zero;
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

            comp.Progress += TimeSpan.FromSeconds(frameTime);
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

        var mob = Spawn(species.Prototype, Transform(uid).Coordinates);
        _humanoid.LoadProfile(mob, profile);
        _metaData.SetEntityName(mob, profile.Name);
        EnsureComp<BankAccountComponent>(mob);
        _traits.ApplyTraits(mob, profile);

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
        comp.FailProgress = TimeSpan.Zero;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Failed);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

    private void ClearPending(LifeInsuranceClonerComponent comp)
    {
        comp.Active = false;
        comp.Progress = TimeSpan.Zero;
        comp.PendingMind = null;
        comp.PendingUser = null;
        comp.PendingProfile = null;
        comp.PendingCompany = "None";
    }

    private void RunFailure(EntityUid uid, LifeInsuranceClonerComponent comp, float frameTime)
    {
        comp.FailProgress += TimeSpan.FromSeconds(frameTime);
        if (comp.FailProgress < comp.FailTime)
            return;

        var coords = Transform(uid).Coordinates;
        Spawn(comp.FailMob, coords);

        // The failed revive bursts out blood.
        var blood = new Solution(comp.FailBloodReagent, comp.FailBloodAmount);
        _puddle.TrySpillAt(coords, blood, out _);

        comp.Failing = false;
        comp.FailProgress = TimeSpan.Zero;
        _appearance.SetData(uid, LifeInsuranceClonerVisuals.State, LifeInsuranceClonerState.Idle);

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }

}
