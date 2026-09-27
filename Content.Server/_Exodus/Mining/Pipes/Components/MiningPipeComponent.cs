using Content.Server._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Mining.Pipes.Components;

/// <summary>
/// Subfloor ore duct segment. Placed and cut like LV cables.
/// </summary>
[RegisterComponent]
[Access(typeof(MiningPipeSystem))]
public sealed partial class MiningPipeComponent : Component
{
    [DataField]
    public EntProtoId PipeDroppedOnCutPrototype = "BulkMiningPipeStack1";

    [DataField]
    public ProtoId<ToolQualityPrototype>? CuttingQuality = SharedToolSystem.CutQuality;

    [DataField]
    public float CuttingDelay = 1f;

    [DataField]
    public MiningPipeType PipeType = MiningPipeType.Ore;
}
