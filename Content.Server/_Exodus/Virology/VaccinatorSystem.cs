// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using System.Text;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared._Exodus.Virology;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology;

public sealed partial class VaccinatorSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private VirologySystem _virology = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VaccinatorComponent, ComponentStartup>(OnUiDirty);
        SubscribeLocalEvent<VaccinatorComponent, BoundUIOpenedEvent>(OnUiDirty);
        SubscribeLocalEvent<VaccinatorComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
        SubscribeLocalEvent<VaccinatorComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
        SubscribeLocalEvent<VaccinatorComponent, VaccinatorScanMessage>(OnScan);
        SubscribeLocalEvent<VaccinatorComponent, VaccinatorTransferMessage>(OnTransfer);
        SubscribeLocalEvent<VaccinatorComponent, VaccinatorCreateVaccineMessage>(OnCreateVaccine);
        SubscribeLocalEvent<VaccinatorComponent, VaccinatorPrintMessage>(OnPrint);
        SubscribeLocalEvent<VaccinatorComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnUiDirty<T>(Entity<VaccinatorComponent> ent, ref T args)
    {
        UpdateUi(ent);
    }

    private void OnSolutionChanged(Entity<VaccinatorComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == ent.Comp.BufferSolutionId)
            UpdateUi(ent);
    }

    private void OnEntInserted(Entity<VaccinatorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        OnSlotChanged(ent, args.Container.ID);
    }

    private void OnEntRemoved(Entity<VaccinatorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        OnSlotChanged(ent, args.Container.ID);
    }

    private void OnSlotChanged(Entity<VaccinatorComponent> ent, string containerId)
    {
        if (containerId != ent.Comp.SlotId)
            return;

        ent.Comp.PrintEndTime = null;
        ent.Comp.ScanEndTime = null;
        ClearResult(ent);
        UpdateUi(ent);
    }

    private void OnScan(Entity<VaccinatorComponent> ent, ref VaccinatorScanMessage args)
    {
        if (!this.IsPowered(ent, EntityManager) || ent.Comp.ScanEndTime != null || ent.Comp.PrintEndTime != null)
            return;

        if (_itemSlots.GetItemOrNull(ent, ent.Comp.SlotId) == null)
            return;

        ClearResult(ent);
        ent.Comp.ScanEndTime = _timing.CurTime + ent.Comp.ScanDuration;
        UpdateUi(ent);
    }

    private void OnTransfer(Entity<VaccinatorComponent> ent, ref VaccinatorTransferMessage args)
    {
        if (!this.IsPowered(ent, EntityManager) || ent.Comp.ScanEndTime != null || ent.Comp.PrintEndTime != null)
            return;

        var item = _itemSlots.GetItemOrNull(ent, ent.Comp.SlotId);
        if (item == null
            || !_solutionContainer.TryGetFitsInDispenser(item.Value, out var sourceSoln, out var source)
            || !_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionId, out var bufferSoln, out var buffer))
            return;

        var available = source.GetTotalPrototypeQuantity(ent.Comp.TricordrazineReagent);
        var amount = FixedPoint2.Min(available, buffer.MaxVolume - buffer.Volume);
        if (amount <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("vaccinator-no-tricordrazine-source"), ent, args.Actor);
            return;
        }

        source.RemoveReagent(new ReagentId(ent.Comp.TricordrazineReagent, null), amount, ignoreReagentData: true);
        _solutionContainer.UpdateChemicals(sourceSoln.Value);
        _solutionContainer.TryAddReagent(bufferSoln.Value, ent.Comp.TricordrazineReagent, amount, out _);
    }

    private void OnCreateVaccine(Entity<VaccinatorComponent> ent, ref VaccinatorCreateVaccineMessage args)
    {
        if (!this.IsPowered(ent, EntityManager) || ent.Comp.ScanEndTime != null || ent.Comp.PrintEndTime != null)
            return;

        var item = _itemSlots.GetItemOrNull(ent, ent.Comp.SlotId);
        if (item == null || !_solutionContainer.TryGetFitsInDispenser(item.Value, out _, out var source))
            return;
        var strains = new HashSet<string>();
        foreach (var virus in VirusData.EnumerateViruses(source))
        {
            if (virus.SuppressedRemaining != null)
                strains.Add(_virology.GetIdentity(virus));
        }

        if (strains.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("vaccinator-no-cured-blood"), ent, args.Actor);
            return;
        }

        if (!_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionId, out var bufferSoln, out var buffer)
            || buffer.GetTotalPrototypeQuantity(ent.Comp.TricordrazineReagent) < ent.Comp.VaccineAmount)
        {
            _popup.PopupEntity(Loc.GetString("vaccinator-no-tricordrazine"), ent, args.Actor);
            return;
        }

        var bottle = Spawn(ent.Comp.VaccineBottle, Transform(ent).Coordinates);
        if (!_solutionContainer.TryGetSolution(bottle, ent.Comp.BottleSolutionId, out var bottleSoln, out var bottleSolution)
            || ent.Comp.VaccineAmount <= FixedPoint2.Zero
            || bottleSolution.AvailableVolume < ent.Comp.VaccineAmount)
        {
            Log.Error($"VaccineBottle {ent.Comp.VaccineBottle} cannot hold the configured output amount in '{ent.Comp.BottleSolutionId}'");
            QueueDel(bottle);
            return;
        }

        _solutionContainer.RemoveReagent(bufferSoln.Value, ent.Comp.TricordrazineReagent, ent.Comp.VaccineAmount);

        var vaccineData = new VirusVaccineData { Strains = [.. strains] };
        _solutionContainer.TryAddReagent(bottleSoln.Value, ent.Comp.VaccineReagent, ent.Comp.VaccineAmount, out _, data: [vaccineData]);

        _popup.PopupEntity(Loc.GetString("vaccinator-vaccine-created"), ent, args.Actor);
    }

    private void OnPrint(Entity<VaccinatorComponent> ent, ref VaccinatorPrintMessage args)
    {
        if (!this.IsPowered(ent, EntityManager) || ent.Comp.ScanEndTime != null || ent.Comp.PrintEndTime != null)
            return;

        if (!ent.Comp.HasResult)
            return;

        ent.Comp.PrintEndTime = _timing.CurTime + ent.Comp.PrintDuration;
        _audio.PlayPvs(ent.Comp.PrintSound, ent);
        UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VaccinatorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            Entity<VaccinatorComponent> ent = (uid, comp);

            if ((comp.ScanEndTime != null || comp.PrintEndTime != null) && !this.IsPowered(ent, EntityManager))
            {
                comp.ScanEndTime = null;
                comp.PrintEndTime = null;
                ClearResult(ent);
                UpdateUi(ent);
                continue;
            }

            if (comp.ScanEndTime is { } scanEnd && _timing.CurTime >= scanEnd)
            {
                comp.ScanEndTime = null;
                BuildResult(ent);
                UpdateUi(ent);
                continue;
            }

            if (comp.PrintEndTime is { } printEnd && _timing.CurTime >= printEnd)
            {
                comp.PrintEndTime = null;
                FinishPrint(ent);
                UpdateUi(ent);
            }
        }
    }

    private void BuildResult(Entity<VaccinatorComponent> ent)
    {
        ClearResult(ent);
        ent.Comp.HasResult = true;

        var item = _itemSlots.GetItemOrNull(ent, ent.Comp.SlotId);
        if (item == null || !_solutionContainer.TryGetFitsInDispenser(item.Value, out _, out var solution))
            return;

        // each virus have its own block
        foreach (var virus in VirusData.EnumerateViruses(solution))
        {
            var block = new VaccinatorVirusResult();

            if (virus.SuppressedRemaining != null)
                block.Suppressed = true;

            var allReadable = true;
            foreach (var snapshot in virus.Symptoms)
            {
                string? description = null;
                var readable = snapshot.Revealed
                    && _virology.TryGetSymptomDescription(snapshot.Symptom, out description)
                    && description != null;

                if (readable)
                    block.Symptoms.Add(_virology.FormatSymptom(snapshot.Symptom, description!, snapshot.Stage));
                else
                {
                    allReadable = false;
                    block.UnreadableCount++;
                }
            }

            if (allReadable && _virology.ResolveName(virus) is { } name)
                block.Name = name;

            // cure reagents only show if every symptom is revealed
            if (_virology.ResolveCure(virus) is { } cure)
            {
                if (allReadable)
                {
                    foreach (var reagent in cure.Reagents)
                    {
                        if (_prototype.TryIndex<ReagentPrototype>(reagent, out var reagentProto))
                            block.CureReagents.Add(reagentProto.LocalizedName);
                    }
                }
                else
                {
                    block.CureHidden = true;
                }
            }

            ent.Comp.ScanResults.Add(block);
        }
    }

    private void FinishPrint(Entity<VaccinatorComponent> ent)
    {
        var report = new StringBuilder();
        foreach (var virus in ent.Comp.ScanResults)
        {
            report.AppendLine(Loc.GetString("pathology-report-pathogen",
                ("name", virus.Name ?? Loc.GetString("pathology-report-pathogen-unknown"))));
            report.AppendLine(Loc.GetString("pathology-report-symptoms"));
            if (virus.Symptoms.Count > 0)
            {
                foreach (var symptom in virus.Symptoms)
                    report.AppendLine($" · {symptom}");
            }
            else
                report.AppendLine(" · —");

            report.AppendLine(Loc.GetString("pathology-report-unreadable", ("count", virus.UnreadableCount)));

            var cure = virus.CureHidden
                ? Loc.GetString("pathology-report-cure-hidden")
                : virus.CureReagents.Count > 0
                    ? string.Join(", ", virus.CureReagents)
                    : "—";
            report.AppendLine(Loc.GetString("pathology-report-cure", ("cure", cure)));
            report.AppendLine();
        }

        var content = Loc.GetString("virology-vaccine-report", ("report", report.ToString().TrimEnd()));

        var paper = Spawn(ent.Comp.ReportPaper, Transform(ent).Coordinates);
        if (TryComp<PaperComponent>(paper, out var paperComp))
            _paper.SetContent((paper, paperComp), content);
    }

    private void UpdateUi(Entity<VaccinatorComponent> ent)
    {
        var hasSample = _itemSlots.GetItemOrNull(ent, ent.Comp.SlotId) != null;
        var scanning = ent.Comp.ScanEndTime != null;
        var printing = ent.Comp.PrintEndTime != null;
        var operationEnd = ent.Comp.ScanEndTime ?? ent.Comp.PrintEndTime;
        var operationDuration = scanning
            ? ent.Comp.ScanDuration
            : printing
                ? ent.Comp.PrintDuration
                : TimeSpan.Zero;

        var status = !hasSample ? VirologyMachineStatus.NoSample
            : scanning ? VirologyMachineStatus.Scanning
            : printing ? VirologyMachineStatus.Printing
            : ent.Comp.HasResult ? VirologyMachineStatus.Result
            : VirologyMachineStatus.Ready;

        var bufferTricordrazine = 0f;
        _solutionContainer.TryGetSolution(ent.Owner, ent.Comp.BufferSolutionId, out _, out var buffer);
        if (buffer != null)
            bufferTricordrazine = (float)buffer.GetTotalPrototypeQuantity(ent.Comp.TricordrazineReagent);

        string? stationName = null;
        if (_station.GetOwningStation(ent.Owner) is { } station)
            stationName = Name(station);

        var state = new VaccinatorBoundUserInterfaceState(
            status,
            operationEnd,
            operationDuration,
            [.. ent.Comp.ScanResults],
            bufferTricordrazine,
            stationName);

        _ui.SetUiState(ent.Owner, VaccinatorUiKey.Key, state);

        UpdateVisuals(ent, buffer);
    }

    private void UpdateVisuals(Entity<VaccinatorComponent> ent, Solution? buffer)
    {
        var fill = 0f;
        if (buffer != null && buffer.MaxVolume > FixedPoint2.Zero)
        {
            var amount = buffer.GetTotalPrototypeQuantity(ent.Comp.TricordrazineReagent);
            fill = Math.Clamp((float)(amount / buffer.MaxVolume), 0f, 1f);
        }

        _appearance.SetData(ent, VaccinatorVisuals.Running, ent.Comp.PrintEndTime != null || ent.Comp.ScanEndTime != null);
        _appearance.SetData(ent, VaccinatorVisuals.Vial, GetVialVisual(ent));
        _appearance.SetData(ent, VaccinatorVisuals.BufferFill, fill);
    }

    private VaccinatorVial GetVialVisual(Entity<VaccinatorComponent> ent)
    {
        if (_itemSlots.GetItemOrNull(ent, ent.Comp.SlotId) is not { } item)
            return VaccinatorVial.None;

        if (!_solutionContainer.TryGetFitsInDispenser(item, out _, out var solution) || solution.Volume <= FixedPoint2.Zero)
            return VaccinatorVial.Empty;

        if (solution.GetTotalPrototypeQuantity(ent.Comp.TricordrazineReagent) > FixedPoint2.Zero)
            return VaccinatorVial.Tricordrazine;

        return VaccinatorVial.Blood;
    }

    private static void ClearResult(Entity<VaccinatorComponent> ent)
    {
        ent.Comp.HasResult = false;
        ent.Comp.ScanResults = [];
    }
}
