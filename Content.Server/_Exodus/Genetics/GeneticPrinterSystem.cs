using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Genetics;

public sealed class GeneticPrinterSystem : EntitySystem
{
    [Dependency] private readonly GeneticDiskSystem _disks = default!;
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<GeneticDiskComponent> _diskQuery;

    public override void Initialize()
    {
        base.Initialize();
        _diskQuery = GetEntityQuery<GeneticDiskComponent>();
        SubscribeLocalEvent<GeneticPrinterComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<GeneticPrinterComponent, GeneticPrinterMessage>(OnMessage);
        SubscribeLocalEvent<GeneticPrinterComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<GeneticPrinterComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<GeneticPrinterComponent, SolutionContainerChangedEvent>(OnSolution);
        SubscribeLocalEvent<GeneticPrinterComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<GeneticDiskChangedEvent>(OnDiskChanged);
    }

    private bool CanOperate(Entity<GeneticPrinterComponent> ent, EntityUid user)
    {
        return !TerminatingOrDeleted(user) && this.IsPowered(ent, EntityManager) &&
               _blocker.CanInteract(user, ent) && _interaction.InRangeAndAccessible(user, ent.Owner);
    }

    private bool HasReagent(Entity<GeneticPrinterComponent> ent, FixedPoint2 cost)
    {
        return cost == 0 || cost > 0 && _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution) &&
               solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) >= cost;
    }

    private void OnMessage(Entity<GeneticPrinterComponent> ent, ref GeneticPrinterMessage args)
    {
        if (!Enum.IsDefined(args.Operation) || ent.Comp.Pending != null || !CanOperate(ent, args.Actor) ||
            _slots.GetItemOrNull(ent, ent.Comp.DiskSlot) is not { } disk || args.Disk != GetNetEntity(disk) ||
            !TryComp<GeneticDiskComponent>(disk, out var data))
            return;
        if (args.Revision != data.Revision)
        {
            UpdateUi(ent);
            return;
        }

        if (args.Operation == GeneticPrinterOperation.ClearDisk)
        {
            _disks.SetSample((disk, data), null);
            _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):user} erased genetic disk {ToPrettyString(disk):target}");
            return;
        }
        if (data.Sample is not { } sample || !_genetics.IsCompatible(sample) || args.Block < 0 || args.Block >= sample.Blocks.Count)
            return;
        if (args.Operation == GeneticPrinterOperation.ResetBlock)
        {
            if (_disks.TryResetBlock((disk, data), args.Block))
                _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.Actor):user} cleared block {args.Block + 1} on genetic disk {ToPrettyString(disk):target}");
            return;
        }

        var cost = args.Operation == GeneticPrinterOperation.PrintGenome ? ent.Comp.GenomeCost : ent.Comp.BlockCost;
        if (!HasReagent(ent, cost))
        {
            _popup.PopupEntity(Loc.GetString("genetics-no-mutagen"), ent, args.Actor);
            return;
        }
        ent.Comp.Pending = new GeneticPrintJob
        {
            User = args.Actor, Disk = disk, Revision = data.Revision,
            Block = args.Operation == GeneticPrinterOperation.PrintBlock ? args.Block : null,
            Cost = cost,
            Sample = new GeneticSnapshot { Context = sample.Context, Blocks = new List<ushort>(sample.Blocks) },
        };
        ent.Comp.EndTime = _timing.CurTime + ent.Comp.PrintDuration;
        ent.Comp.NextValidation = _timing.CurTime;
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticPrinterComponent>();
        while (query.MoveNext(out var uid, out var printer))
        {
            if (printer.Pending is not { } job || printer.NextValidation > _timing.CurTime)
                continue;
            printer.NextValidation += TimeSpan.FromSeconds(0.5);
            if (!CanOperate((uid, printer), job.User) || _slots.GetItemOrNull(uid, printer.DiskSlot) != job.Disk ||
                TerminatingOrDeleted(job.Disk) || !_diskQuery.TryGetComponent(job.Disk, out var disk) || disk.Revision != job.Revision ||
                !_genetics.IsCompatible(job.Sample) || !HasReagent((uid, printer), job.Cost))
            {
                printer.Pending = null;
                UpdateUi((uid, printer));
                continue;
            }
            if (_timing.CurTime < printer.EndTime)
                continue;
            printer.Pending = null;
            Finish((uid, printer), job);
        }
    }

    private void Finish(Entity<GeneticPrinterComponent> ent, GeneticPrintJob job)
    {
        var output = Spawn(ent.Comp.Injector, Transform(ent).Coordinates);
        if (!TryComp<GeneticInjectorComponent>(output, out var injector))
        {
            QueueDel(output);
            UpdateUi(ent);
            return;
        }
        injector.Context = job.Sample.Context;
        if (job.Block is { } block)
        {
            injector.Block = block;
            injector.Value = job.Sample.Blocks[block];
        }
        else
            injector.Sample = job.Sample;
        _metadata.SetEntityName(output, job.Block is { } number
            ? Loc.GetString("genetics-injector-block-name", ("number", number + 1))
            : Loc.GetString("genetics-injector-genome-name"));
        if (job.Cost > 0 && _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var solution, out _))
            _solutions.RemoveReagent(solution.Value, ent.Comp.Reagent, job.Cost);
        _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(job.User):user} printed {ToPrettyString(output):target} from genetic disk {ToPrettyString(job.Disk)}");
        UpdateUi(ent);
    }

    private void OnOpened(Entity<GeneticPrinterComponent> ent, ref BoundUIOpenedEvent args) => UpdateUi(ent);
    private void OnSolution(Entity<GeneticPrinterComponent> ent, ref SolutionContainerChangedEvent args) => UpdateUi(ent);

    private void OnPower(Entity<GeneticPrinterComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            ent.Comp.Pending = null;
        UpdateUi(ent);
    }

    private void OnInserted(Entity<GeneticPrinterComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        OnContainerChanged(ent, args.Container.ID);
    }

    private void OnRemoved(Entity<GeneticPrinterComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        OnContainerChanged(ent, args.Container.ID);
    }

    private void OnContainerChanged(Entity<GeneticPrinterComponent> ent, string id)
    {
        if (id != ent.Comp.DiskSlot)
            return;
        ent.Comp.Pending = null;
        UpdateUi(ent);
    }

    private void OnDiskChanged(ref GeneticDiskChangedEvent args)
    {
        var query = EntityQueryEnumerator<GeneticPrinterComponent>();
        while (query.MoveNext(out var uid, out var printer))
        {
            if (_slots.GetItemOrNull(uid, printer.DiskSlot) != args.Disk)
                continue;
            printer.Pending = null;
            UpdateUi((uid, printer));
        }
    }

    private void UpdateUi(Entity<GeneticPrinterComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;
        var mutagen = _solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
            ? (float) solution.GetTotalPrototypeQuantity(ent.Comp.Reagent) : 0;
        var state = new GeneticPrinterUiState(_disks.GetData(_slots.GetItemOrNull(ent, ent.Comp.DiskSlot)),
            this.IsPowered(ent, EntityManager), ent.Comp.Pending != null, mutagen);
        _ui.SetUiState(ent.Owner, GeneticsUiKey.Printer, state);
    }
}
