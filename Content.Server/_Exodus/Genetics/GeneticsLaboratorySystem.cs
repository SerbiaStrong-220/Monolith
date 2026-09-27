using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Genetics;

public sealed class GeneticsLaboratorySystem : EntitySystem
{
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly GeneticDiskSystem _disks = default!;
    [Dependency] private readonly MedicalScannerSystem _scanner = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticsLaboratoryComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<GeneticsLaboratoryComponent, GeneticsMessage>(OnMessage);
        SubscribeLocalEvent<GeneticsLaboratoryComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<GeneticsLaboratoryComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<GeneticsLaboratoryComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<GeneticsLaboratoryComponent, SolutionContainerChangedEvent>(OnSolution);
        SubscribeLocalEvent<GenomeComponent, GenomeChangedEvent>(OnGenomeChanged);
        SubscribeLocalEvent<GenomeComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private EntityUid? Patient(Entity<GeneticsLaboratoryComponent> ent)
    {
        return TryComp<MedicalScannerComponent>(ent, out var scanner) ? scanner.BodyContainer?.ContainedEntity : null;
    }

    private void OnOpened(Entity<GeneticsLaboratoryComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnInserted(Entity<GeneticsLaboratoryComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        OnContainerChanged(ent, args.Container.ID);
    }

    private void OnRemoved(Entity<GeneticsLaboratoryComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        OnContainerChanged(ent, args.Container.ID);
    }

    private void OnContainerChanged(Entity<GeneticsLaboratoryComponent> ent, string id)
    {
        if (id == "scanner-bodyContainer")
        {
            ent.Comp.Pending = null;
            ent.Comp.ScannedPatient = null;
            ent.Comp.ScannedRevision = -1;
        }
        UpdateUi(ent);
    }

    private void OnPower(Entity<GeneticsLaboratoryComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            ent.Comp.Pending = null;
        UpdateUi(ent);
    }

    private void OnSolution(Entity<GeneticsLaboratoryComponent> ent, ref SolutionContainerChangedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnGenomeChanged(Entity<GenomeComponent> ent, ref GenomeChangedEvent args)
    {
        var labs = EntityQueryEnumerator<GeneticsLaboratoryComponent>();
        while (labs.MoveNext(out var uid, out var lab))
        {
            if (Patient((uid, lab)) == ent.Owner)
                UpdateUi((uid, lab));
        }
    }

    private void OnMobStateChanged(Entity<GenomeComponent> ent, ref MobStateChangedEvent args)
    {
        var labs = EntityQueryEnumerator<GeneticsLaboratoryComponent>();
        while (labs.MoveNext(out var uid, out var lab))
        {
            if (Patient((uid, lab)) != ent.Owner)
                continue;
            if (!_genetics.IsLivingSubject(ent))
            {
                lab.Pending = null;
                lab.ScannedPatient = null;
                lab.ScannedRevision = -1;
            }
            UpdateUi((uid, lab));
        }
    }

    private bool CanAccess(Entity<GeneticsLaboratoryComponent> ent, EntityUid user)
    {
        return !TerminatingOrDeleted(ent) && !TerminatingOrDeleted(user) &&
               _blocker.CanInteract(user, ent) && _interaction.InRangeAndAccessible(user, ent.Owner);
    }

    private bool CanOperate(Entity<GeneticsLaboratoryComponent> ent, EntityUid user)
    {
        return CanAccess(ent, user) && this.IsPowered(ent, EntityManager);
    }

    private void OnMessage(Entity<GeneticsLaboratoryComponent> ent, ref GeneticsMessage args)
    {
        if (!Enum.IsDefined(args.Operation) || !CanAccess(ent, args.Actor))
            return;

        // Mechanical ejection remains available without power, a scan or a living patient, including during procedures.
        if (args.Operation == GeneticsOperation.EjectPatient)
        {
            if (Patient(ent) is { } occupant && args.Patient == GetNetEntity(occupant) &&
                TryComp<MedicalScannerComponent>(ent, out var scanner))
                _scanner.EjectBody(ent.Owner, scanner);
            UpdateUi(ent);
            return;
        }
        if (args.Operation == GeneticsOperation.EjectDisk)
        {
            if (_slots.TryGetSlot(ent.Owner, ent.Comp.DiskSlot, out var slot))
                _slots.TryEjectToHands(ent.Owner, slot, args.Actor);
            UpdateUi(ent);
            return;
        }

        if (ent.Comp.Pending != null || !this.IsPowered(ent, EntityManager) ||
            Patient(ent) is not { } patient || args.Patient != GetNetEntity(patient) ||
            !_genetics.TryGetLivingGenome(patient, out var genome))
            return;

        // Ordinary labs print from the scanned patient; reading saved samples and using buffers remain ADMIN-only.
        if (!ent.Comp.Debug && args.Operation is not (GeneticsOperation.Scan or GeneticsOperation.Edit or GeneticsOperation.WriteDisk or
                GeneticsOperation.PrintInjector or GeneticsOperation.PrintGenome))
            return;

        if (args.Operation != GeneticsOperation.Scan &&
            (ent.Comp.ScannedPatient != patient || ent.Comp.ScannedRevision != genome.Revision || args.Revision != genome.Revision))
        {
            Fail(ent, args.Actor, "genetics-rescan");
            return;
        }

        if (args.Block < 0 || args.Block >= genome.Blocks.Count || args.Buffer < 0 || args.Buffer >= ent.Comp.Buffers.Length ||
            args.Value < 0 || args.Value > GeneticsSystem.MaxBlockValue || args.Digit is < 0 or > 2)
            return;

        var pending = new GeneticsPendingOperation
        {
            User = args.Actor, Patient = patient, Revision = genome.Revision,
            Operation = args.Operation, Block = args.Block, Value = (ushort) args.Value, Digit = args.Digit,
        };
        switch (args.Operation)
        {
            case GeneticsOperation.StoreBuffer:
                ent.Comp.Buffers[args.Buffer] = _genetics.Capture((patient, genome));
                UpdateUi(ent);
                return;
            case GeneticsOperation.WriteDisk:
                if (_slots.GetItemOrNull(ent, ent.Comp.DiskSlot) is { } disk && TryComp<GeneticDiskComponent>(disk, out var data))
                {
                    _disks.SetSample((disk, data), _genetics.Capture((patient, genome)));
                    _popup.PopupEntity(Loc.GetString("genetics-disk-written"), ent, args.Actor);
                }
                UpdateUi(ent);
                return;
            case GeneticsOperation.ReadDisk:
                if (_slots.GetItemOrNull(ent, ent.Comp.DiskSlot) is not { } input ||
                    !TryComp<GeneticDiskComponent>(input, out var diskData) || diskData.Sample is not { } sample ||
                    !_genetics.IsCompatible(sample))
                {
                    Fail(ent, args.Actor, "genetics-invalid-sample");
                    return;
                }
                ent.Comp.Buffers[args.Buffer] = new GeneticSnapshot { Context = sample.Context, Blocks = new List<ushort>(sample.Blocks) };
                UpdateUi(ent);
                return;
            case GeneticsOperation.RestoreBuffer:
                if (ent.Comp.Buffers[args.Buffer] is not { } buffer || !_genetics.IsCompatible(buffer))
                {
                    Fail(ent, args.Actor, "genetics-invalid-sample");
                    return;
                }
                pending.Sample = new GeneticSnapshot { Context = buffer.Context, Blocks = new List<ushort>(buffer.Blocks) };
                pending.Cost = ent.Comp.RestoreCost;
                break;
            case GeneticsOperation.Reset:
                pending.Sample = new GeneticSnapshot { Context = genome.Context, Blocks = new List<ushort>(genome.Baseline) };
                pending.Cost = ent.Comp.RestoreCost;
                break;
            case GeneticsOperation.Edit:
            case GeneticsOperation.SetBlock:
                pending.Cost = ent.Comp.EditCost;
                break;
            case GeneticsOperation.PrintInjector:
                pending.Value = genome.Blocks[args.Block];
                pending.Cost = ent.Comp.InjectorCost;
                break;
            case GeneticsOperation.PrintGenome:
                pending.Sample = _genetics.Capture((patient, genome));
                pending.Cost = ent.Comp.GenomeInjectorCost;
                break;
            case GeneticsOperation.PrintBufferInjector:
            case GeneticsOperation.PrintBufferGenome:
                if (ent.Comp.Buffers[args.Buffer] is not { } stored || !_genetics.IsCompatible(stored))
                {
                    Fail(ent, args.Actor, "genetics-invalid-sample");
                    return;
                }
                pending.Value = stored.Blocks[args.Block];
                pending.Sample = new GeneticSnapshot { Context = stored.Context, Blocks = new List<ushort>(stored.Blocks) };
                pending.Cost = args.Operation == GeneticsOperation.PrintBufferGenome ? ent.Comp.GenomeInjectorCost : ent.Comp.InjectorCost;
                break;
        }
        if (!HasReagent(ent, pending.Cost))
        {
            Fail(ent, args.Actor, "genetics-no-mutagen");
            return;
        }
        ent.Comp.Pending = pending;
        ent.Comp.NextValidation = _timing.CurTime;
        ent.Comp.EndTime = _timing.CurTime + (args.Operation == GeneticsOperation.Scan ? ent.Comp.ScanDuration : ent.Comp.EditDuration);
        UpdateUi(ent);
    }

    private bool HasReagent(Entity<GeneticsLaboratoryComponent> ent, FixedPoint2 cost)
    {
        return cost == 0 || cost > 0 && _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution) &&
            solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) >= cost;
    }

    private void Fail(Entity<GeneticsLaboratoryComponent> ent, EntityUid user, LocId message)
    {
        _popup.PopupEntity(Loc.GetString(message), ent, user);
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticsLaboratoryComponent>();
        while (query.MoveNext(out var uid, out var lab))
        {
            if (lab.Pending is not { } pending)
                continue;
            if (_timing.CurTime < lab.NextValidation)
                continue;
            lab.NextValidation += TimeSpan.FromSeconds(0.5);
            if (!CanOperate((uid, lab), pending.User) || Patient((uid, lab)) != pending.Patient ||
                !_genetics.IsLivingSubject(pending.Patient) ||
                !TryComp<GenomeComponent>(pending.Patient, out var genome) || genome.Revision != pending.Revision)
            {
                lab.Pending = null;
                UpdateUi((uid, lab));
                continue;
            }
            if (_timing.CurTime < lab.EndTime)
                continue;
            lab.Pending = null; // A synchronous genome/solution event cannot complete the operation twice.
            Finish((uid, lab), pending, genome);
        }
    }

    private void Finish(Entity<GeneticsLaboratoryComponent> ent, GeneticsPendingOperation pending, GenomeComponent genome)
    {
        if (!_genetics.IsLivingSubject(pending.Patient))
            return;
        if (!HasReagent(ent, pending.Cost))
        {
            Fail(ent, pending.User, "genetics-no-mutagen");
            return;
        }
        var target = new Entity<GenomeComponent>(pending.Patient, genome);
        EntityUid? output = null;
        if (pending.Operation is GeneticsOperation.PrintInjector or GeneticsOperation.PrintGenome or
            GeneticsOperation.PrintBufferInjector or GeneticsOperation.PrintBufferGenome)
        {
            output = Spawn(ent.Comp.Injector, Transform(ent).Coordinates);
            if (!TryComp<GeneticInjectorComponent>(output, out var injector))
            {
                QueueDel(output.Value);
                Fail(ent, pending.User, "genetics-invalid-sample");
                return;
            }
            injector.Context = genome.Context;
            injector.Block = pending.Block;
            injector.Value = pending.Value;
            if (pending.Operation is GeneticsOperation.PrintGenome or GeneticsOperation.PrintBufferGenome)
                injector.Sample = pending.Sample;
            _metadata.SetEntityName(output.Value, injector.Sample == null
                ? Loc.GetString("genetics-injector-block-name", ("number", pending.Block + 1))
                : Loc.GetString("genetics-injector-genome-name"));
        }
        if (pending.Cost > 0 && _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var buffer, out _))
            _solutions.RemoveReagent(buffer.Value, ent.Comp.Reagent, pending.Cost);

        switch (pending.Operation)
        {
            case GeneticsOperation.Edit:
                if (_genetics.TryRandomizeDigit(target, pending.Block, pending.Digit, pending.User))
                    _damage.TryChangeDamage(target, ent.Comp.EditDamage, true, false, ignoreGlobalModifiers: true);
                break;
            case GeneticsOperation.SetBlock:
                _genetics.TrySetBlock(target, pending.Block, pending.Value, pending.User);
                break;
            case GeneticsOperation.Reset:
            case GeneticsOperation.RestoreBuffer:
                if (pending.Sample != null)
                    _genetics.TryApply(target, pending.Sample, pending.User);
                break;
        }
        var living = _genetics.IsLivingSubject(pending.Patient);
        ent.Comp.ScannedPatient = living ? pending.Patient : null;
        ent.Comp.ScannedRevision = living ? genome.Revision : -1;
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<GeneticsLaboratoryComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;
        var patient = Patient(ent);
        var blocks = new List<GeneticBlockInfo>();
        var revision = -1;
        int? stability = null;
        var living = patient is { } subject && _genetics.IsLivingSubject(subject);
        if (living && patient is { } uid && TryComp<GenomeComponent>(uid, out var genome) &&
            ent.Comp.ScannedPatient == uid && ent.Comp.ScannedRevision == genome.Revision)
        {
            revision = genome.Revision;
            if (ent.Comp.Debug)
                stability = genome.Stability;
            var round = _genetics.GetRound();
            for (var i = 0; i < genome.Blocks.Count && i < round.Mutations.Count; i++)
            {
                if (!ent.Comp.Debug)
                {
                    blocks.Add(new GeneticBlockInfo(genome.Blocks[i]));
                    continue;
                }
                if (round.Mutations[i] is not { } mutation)
                {
                    blocks.Add(new GeneticBlockInfo(genome.Blocks[i], Loc.GetString("genetics-empty-block")));
                    continue;
                }
                var prototype = _prototypes.Index(mutation);
                blocks.Add(new GeneticBlockInfo(genome.Blocks[i], Loc.GetString(prototype.Name),
                    Loc.GetString(prototype.Description), genome.Active.Contains(mutation)));
            }
        }
        var buffers = new bool[ent.Comp.Buffers.Length];
        for (var i = 0; i < buffers.Length; i++)
            buffers[i] = ent.Comp.Buffers[i] is { } sample && _genetics.IsCompatible(sample);
        var reagent = _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
            ? (float) solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) : 0;
        var state = new GeneticsUiState(patient == null ? null : GetNetEntity(patient.Value),
            patient == null ? Loc.GetString("genetics-no-patient") : Identity.Name(patient.Value, EntityManager),
            revision, stability, this.IsPowered(ent, EntityManager), ent.Comp.Pending != null, reagent, blocks, buffers,
            _slots.GetItemOrNull(ent, ent.Comp.DiskSlot) != null, ent.Comp.Debug, living);
        _ui.SetUiState(ent.Owner, GeneticsUiKey.Laboratory, state);
    }
}
