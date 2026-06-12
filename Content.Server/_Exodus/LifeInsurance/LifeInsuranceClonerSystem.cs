using Content.Server.EUI;
using Content.Server.Humanoid;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Preferences;
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

        return !comp.Active && _backup.IsOperational(uid);
    }

    /// <summary>
    /// Begins growing a clone from the given profile. The mind is transferred once revival completes.
    /// </summary>
    public bool TryStartRevival(EntityUid uid, HumanoidCharacterProfile profile, EntityUid mindId, NetUserId user, LifeInsuranceClonerComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return false;

        if (comp.Active || !_backup.IsOperational(uid))
            return false;

        if (!_prototype.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            return false;

        var mob = Spawn(species.Prototype, _transform.GetMapCoordinates(uid));
        _humanoid.LoadProfile(mob, profile);
        _metaData.SetEntityName(mob, profile.Name);
        EnsureComp<BankAccountComponent>(mob);

        if (!_container.Insert(mob, comp.BodyContainer))
        {
            QueueDel(mob);
            return false;
        }

        comp.Active = true;
        comp.Progress = 0f;
        comp.PendingMind = mindId;
        comp.PendingUser = user;

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LifeInsuranceClonerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active)
                continue;

            // Backup battery keeps this running through outages; if it runs out, pause until power returns.
            if (!_backup.IsOperational(uid))
                continue;

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

        if (comp.ConnectedConsole is { } console)
            _console.UpdateUi(console);
    }
}
