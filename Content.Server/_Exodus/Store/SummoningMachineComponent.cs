using Content.Shared._Exodus.Store;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Store;

[RegisterComponent]
[Access(typeof(SummoningMachineSystem))]
public sealed partial class SummoningMachineComponent : Component
{
    [DataField("durationMultiplier"), ViewVariables(VVAccess.ReadWrite)]
    public float DurationMultiplier = 1f;

    [DataField("secondsPerCostUnit"), ViewVariables(VVAccess.ReadWrite)]
    public float SecondsPerCostUnit = 1f;

    [DataField("ejectSpeed"), ViewVariables(VVAccess.ReadWrite)]
    public float EjectSpeed = 6f;

    [DataField("uiUpdateInterval"), ViewVariables(VVAccess.ReadWrite)]
    public float UiUpdateInterval = 0.25f;

    public ProtoId<ListingPrototype>? ActiveListingId;
    public EntProtoId? ActiveProductEntity;
    public TimeSpan ActiveDuration = TimeSpan.Zero;
    public TimeSpan RemainingDuration = TimeSpan.Zero;
    public float UiAccumulator;
    public SummoningMachineVisualState VisualState = SummoningMachineVisualState.Inactive;
}
