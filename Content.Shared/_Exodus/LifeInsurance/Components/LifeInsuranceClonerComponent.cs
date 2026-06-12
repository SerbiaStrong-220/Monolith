using Robust.Shared.Containers;
using Robust.Shared.Network;

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
}
