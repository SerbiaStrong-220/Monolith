using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class GeneticPrinterComponent : Component
{
    /// <summary>Item slot containing the source disk.</summary>
    [DataField] public string DiskSlot = "genetics-disk";
    /// <summary>Reservoir used to manufacture injectors.</summary>
    [DataField] public string Solution = "buffer";
    [DataField] public ProtoId<ReagentPrototype> Reagent = "UnstableMutagen";
    /// <summary>Reagent consumed upon successful printing of a single block.</summary>
    [DataField] public FixedPoint2 BlockCost = 10;
    /// <summary>Reagent consumed upon successful printing of an entire sample.</summary>
    [DataField] public FixedPoint2 GenomeCost = 30;
    [DataField] public EntProtoId Injector = "GeneticInjector";
    [DataField] public TimeSpan PrintDuration = TimeSpan.FromSeconds(6);
    /// <summary>Completion time of the current job.</summary>
    [DataField, AutoPausedField] public TimeSpan EndTime;
    /// <summary>Next check that the operator and source disk are still available.</summary>
    [DataField, AutoPausedField] public TimeSpan NextValidation;
    /// <summary>Runtime-only job, discarded if the machine loses access to its source or operator.</summary>
    public GeneticPrintJob? Pending;
}

/// <summary>A disk revision and independent sample captured when printing begins.</summary>
public sealed class GeneticPrintJob
{
    public EntityUid User;
    public EntityUid Disk;
    public int Revision;
    public int? Block;
    public FixedPoint2 Cost;
    public GeneticSnapshot Sample = default!;
}
