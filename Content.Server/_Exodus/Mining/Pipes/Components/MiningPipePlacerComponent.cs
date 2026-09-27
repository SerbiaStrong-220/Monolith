using Content.Shared._Exodus.Mining.Pipes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Mining.Pipes.Components;

[RegisterComponent]
public sealed partial class MiningPipePlacerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId? PipePrototypeId;

    [DataField]
    public MiningPipeType BlockingPipeType = MiningPipeType.Ore;
}
