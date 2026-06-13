using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.LifeInsurance.Components;

/// <summary>
/// Cloning capsule. Rebuilds an insured player's body from a recorded profile when their
/// ghost activates life insurance, then transfers their mind into the new body.
/// </summary>
[RegisterComponent]
public sealed partial class LifeInsuranceClonerComponent : Component
{
    /// <summary>
    /// How long the revival/cloning process takes once started, in seconds.
    /// </summary>
    [DataField]
    public float RevivalTime = 10f;

    /// <summary>
    /// Console this cloner is linked to.
    /// </summary>
    [ViewVariables]
    public EntityUid? ConnectedConsole;

    /// <summary>
    /// Container holding the body currently being grown.
    /// </summary>
    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    /// <summary>
    /// True while a revival is in progress.
    /// </summary>
    [ViewVariables]
    public bool Active;

    /// <summary>
    /// Elapsed revival time.
    /// </summary>
    [ViewVariables]
    public float Progress;

    /// <summary>
    /// Mind to transfer into the clone once the process completes.
    /// </summary>
    [ViewVariables]
    public EntityUid? PendingMind;

    /// <summary>
    /// User whose insurance charge is being consumed, for console bookkeeping.
    /// </summary>
    [ViewVariables]
    public NetUserId? PendingUser;

    /// <summary>
    /// True while a failed batch is decaying before spitting out a botched abomination.
    /// Triggered when power is fully lost (grid down and backup battery depleted) mid-revival.
    /// </summary>
    [ViewVariables]
    public bool Failing;

    /// <summary>
    /// Elapsed failure decay time.
    /// </summary>
    [ViewVariables]
    public float FailProgress;

    /// <summary>
    /// How long the gory failure state lasts before the abomination crawls out, in seconds.
    /// </summary>
    [DataField]
    public float FailTime = 30f;

    /// <summary>
    /// Hostile mob spawned from a failed clone batch (a botched, unfinished body).
    /// </summary>
    [DataField]
    public EntProtoId FailMob = "MobHorrorExpeditions";
}

[Serializable, NetSerializable]
public enum LifeInsuranceClonerVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum LifeInsuranceClonerState : byte
{
    Idle,
    Cloning,
    Failed
}
