using Content.Shared._Exodus.Store;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Store;

[RegisterComponent, AutoGenerateComponentPause]
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

    /// <summary>
    /// Powered idle time available to pay for future summons. Preserved while the machine is unpowered.
    /// </summary>
    [DataField]
    public TimeSpan StoredTime = TimeSpan.Zero;

    public ProtoId<ListingPrototype>? ActiveListingId;
    public EntProtoId? ActiveProductEntity;
    public TimeSpan ActiveDuration = TimeSpan.Zero;
    public TimeSpan RemainingDuration = TimeSpan.Zero;

    /// <summary>
    /// Next update of the open store's summoning timers.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextUiUpdate;

    public SummoningMachineVisualState VisualState = SummoningMachineVisualState.Inactive;
}
