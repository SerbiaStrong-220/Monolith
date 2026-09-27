using Content.Shared._Exodus.Genetics;
using Content.Shared.Examine;
using Robust.Server.GameObjects;

namespace Content.Server._Exodus.Genetics;

[ByRefEvent]
public readonly record struct GeneticDiskChangedEvent(EntityUid Disk);

public sealed class GeneticDiskSystem : EntitySystem
{
    [Dependency] private readonly GeneticsSystem _genetics = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticDiskComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<GeneticDiskComponent, ExaminedEvent>(OnExamined);
    }

    public GeneticDiskData GetData(EntityUid? disk)
    {
        if (disk is not { } uid || TerminatingOrDeleted(uid) || !TryComp<GeneticDiskComponent>(uid, out var data))
            return new GeneticDiskData(null, -1, GeneticDiskStatus.Missing, new());

        var status = data.Sample == null ? GeneticDiskStatus.Empty
            : _genetics.IsCompatible(data.Sample) ? GeneticDiskStatus.Ready : GeneticDiskStatus.Incompatible;
        return new GeneticDiskData(GetNetEntity(uid), data.Revision, status,
            data.Sample == null ? new() : new List<ushort>(data.Sample.Blocks));
    }

    /// <summary>Stores an independent snapshot so disks, buffers and printed injectors never alias each other.</summary>
    public void SetSample(Entity<GeneticDiskComponent> ent, GeneticSnapshot? sample)
    {
        ent.Comp.Sample = sample == null ? null
            : new GeneticSnapshot { Context = sample.Context, Blocks = new List<ushort>(sample.Blocks) };
        ent.Comp.Revision++;
        UpdateUi(ent);
        var changed = new GeneticDiskChangedEvent(ent.Owner);
        RaiseLocalEvent(ref changed);
    }

    public bool TryResetBlock(Entity<GeneticDiskComponent> ent, int block)
    {
        if (ent.Comp.Sample is not { } sample || !_genetics.IsCompatible(sample) || block < 0 || block >= sample.Blocks.Count)
            return false;

        var copy = new GeneticSnapshot { Context = sample.Context, Blocks = new List<ushort>(sample.Blocks) };
        copy.Blocks[block] = 0;
        SetSample(ent, copy);
        return true;
    }

    private void OnOpened(Entity<GeneticDiskComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<GeneticDiskComponent> ent)
    {
        _ui.SetUiState(ent.Owner, GeneticsUiKey.Disk, new GeneticDiskUiState(GetData(ent.Owner)));
    }

    private void OnExamined(Entity<GeneticDiskComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        args.PushMarkup(ent.Comp.Sample is { } sample
            ? Loc.GetString("genetics-disk-examine-recorded", ("count", sample.Blocks.Count))
            : Loc.GetString("genetics-disk-examine-empty"));
    }
}
