using Content.Shared._Exodus.Genetics;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class GeneticsLaboratoryComponent : Component
{
    /// <summary>ADMIN-only prototype: reveals genes and enables exact edits and direct restoration.</summary>
    [DataField] public bool Debug;
    /// <summary>Applied to the patient once per completed digit randomization, including unchanged rolls.</summary>
    [DataField] public DamageSpecifier EditDamage = new() { DamageDict = new() { ["Poison"] = 10 } };
    [DataField] public string DiskSlot = "genetics-disk";
    [DataField] public string Solution = "buffer";
    [DataField] public ProtoId<ReagentPrototype> Reagent = "UnstableMutagen";
    [DataField] public FixedPoint2 EditCost = 5;
    [DataField] public FixedPoint2 RestoreCost = 15;
    [DataField] public FixedPoint2 InjectorCost = 10;
    /// <summary>Reagent cost for copying a complete genome into one injector.</summary>
    [DataField] public FixedPoint2 GenomeInjectorCost = 30;
    [DataField] public EntProtoId Injector = "GeneticInjector";
    [DataField] public TimeSpan ScanDuration = TimeSpan.FromSeconds(4);
    [DataField] public TimeSpan EditDuration = TimeSpan.FromSeconds(6);
    [DataField] public GeneticSnapshot?[] Buffers = new GeneticSnapshot?[3];
    [DataField, AutoPausedField] public TimeSpan EndTime;
    [DataField, AutoPausedField] public TimeSpan NextValidation;
    public GeneticsPendingOperation? Pending;
    public EntityUid? ScannedPatient;
    public int ScannedRevision = -1;
}

public sealed class GeneticsPendingOperation
{
    public GeneticsOperation Operation;
    public EntityUid User;
    public EntityUid Patient;
    public int Revision;
    public int Block;
    public int Digit;
    public ushort Value;
    public FixedPoint2 Cost;
    public GeneticSnapshot? Sample;
}
