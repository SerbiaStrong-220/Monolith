using Content.Shared._Mono.Company;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.LifeInsurance.Components;

[RegisterComponent]
public sealed partial class LifeInsuranceClonerComponent : Component
{
    /// <summary>
    /// How long the revival/cloning process takes once started, in seconds.
    /// </summary>
    [DataField]
    public TimeSpan RevivalTime = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Console this cloner is linked to.
    /// </summary>
    [ViewVariables]
    public EntityUid? ConnectedConsole;

    /// <summary>
    /// True while a revival is in progress.
    /// </summary>
    [ViewVariables]
    public bool Active;

    /// <summary>
    /// Elapsed revival time.
    /// </summary>
    [ViewVariables]
    public TimeSpan Progress;

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
    /// Profile the body will be built from once the process completes. The body is not spawned until then.
    /// </summary>
    [ViewVariables]
    public HumanoidCharacterProfile? PendingProfile;

    /// <summary>
    /// Company/faction to restore on the finished clone.
    /// </summary>
    [ViewVariables]
    public ProtoId<CompanyPrototype> PendingCompany = "None";

    /// <summary>
    /// True when cloning is failed (power is fully lost mid-revival).
    /// </summary>
    [ViewVariables]
    public bool Failing;

    /// <summary>
    /// Elapsed failure decay time.
    /// </summary>
    [ViewVariables]
    public TimeSpan FailProgress;

    /// <summary>
    /// How long the gory failure state lasts before the abomination crawls out, in seconds.
    /// </summary>
    [DataField]
    public TimeSpan FailTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ProtoID of mob spawned from a failed clone.
    /// </summary>
    [DataField]
    public EntProtoId FailMob = "MobHorrorExpeditions";

    /// <summary>
    /// Units of blood gushed onto the floor under the abomination when a failed batch finishes.
    /// </summary>
    [DataField]
    public FixedPoint2 FailBloodAmount = 100;

    /// <summary>
    /// Reagent spilled under the abomination on failure.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> FailBloodReagent = "Blood";
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
