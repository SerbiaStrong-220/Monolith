using Content.Shared._Exodus.Store;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Store;

[RegisterComponent]
[Access(typeof(SummoningMachineSystem))]
public sealed partial class SummoningMachineComponent : Component
{
    [DataField("durationMultiplier")]
    public float DurationMultiplier = 1f;

    [DataField("secondsPerCostUnit")]
    public float SecondsPerCostUnit = 1f;

    [DataField("ejectSpeed")]
    public float EjectSpeed = 6f;

    [DataField("uiUpdateInterval")]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.25);

    public ProtoId<ListingPrototype>? ActiveListingId;
    public EntProtoId? ActiveProductEntity;
    public TimeSpan ActiveDuration = TimeSpan.Zero;
    public TimeSpan RemainingDuration = TimeSpan.Zero;
    public TimeSpan UiAccumulator = TimeSpan.Zero;
    public SummoningMachineVisualState VisualState = SummoningMachineVisualState.Inactive;
}
