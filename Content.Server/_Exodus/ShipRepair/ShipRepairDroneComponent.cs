using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Server._Exodus.ShipRepair;

/// <summary>Configuration and runtime state of one autonomous snapshot repair tool.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ShipRepairDroneComponent : Component
{
    /// <summary>Zero repairs one operation; one collects all reachable work in a 3x3 area.</summary>
    [DataField]
    public int RepairRadius;

    /// <summary>Actual work time is divided by this value.</summary>
    [DataField]
    public float RepairThroughput = 1f;

    /// <summary>Allows crossing obstacles, while retaining the projectile collision layer.</summary>
    [DataField]
    public bool CanPhase;

    /// <summary>Distance from a repair operation to a usable work position.</summary>
    [DataField]
    public float RepairRange = 1.6f;

    /// <summary>Maximum distance in tiles for repairing a hull barrier through static hull obstructions.</summary>
    [DataField]
    public float StructuralRepairTileRange = 4f;

    /// <summary>Empty space included outside the saved/current hull for exterior navigation.</summary>
    [DataField]
    public float ExteriorMargin = 8f;

    /// <summary>Maximum relative ship speed when the drone is outside its assigned grid.</summary>
    [DataField]
    public float MaximumShipSpeed = 3f;

    /// <summary>Preferred navigation radius, never smaller than the actual collision body.</summary>
    [DataField]
    public float Clearance = 0.28f;

    /// <summary>Maximum distance of a local move to regain navigation clearance.</summary>
    [DataField]
    public float ClearanceRecoveryDistance = 1f;

    /// <summary>Spacing of candidate rings for a short, collision-checked escape.</summary>
    [DataField]
    public float ClearanceRecoveryStep = 0.25f;

    /// <summary>Arrival tolerance for the local escape, independent of normal route waypoints.</summary>
    [DataField]
    public float ClearanceRecoveryRange = 0.05f;

    /// <summary>Time allowed for one short recovery movement before releasing its destination.</summary>
    [DataField]
    public TimeSpan ClearanceRecoveryTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Delay between bounded local probes when there is no safe escape.</summary>
    [DataField]
    public TimeSpan ClearanceRecoveryRetry = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextClearanceRecovery;

    [DataField, AutoPausedField]
    public TimeSpan ClearanceRecoveryDeadline;

    [ViewVariables]
    public ShipRepairClearanceState ClearanceState;

    [ViewVariables]
    public ShipRepairNavigationIssue NavigationIssue;

    public Vector2 ClearanceDestination;

    [DataField]
    public int PathNodeLimit = 8192;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.2);

    [DataField]
    public TimeSpan IdleInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan StuckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Hard limit on travel/search for one job, independent of route retries.</summary>
    [DataField]
    public TimeSpan NavigationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Initial path search budget; an unfinished search is deferred, not declared impossible.</summary>
    [DataField]
    public TimeSpan SearchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Budget for a deferred search, after giving fresh work priority.</summary>
    [DataField]
    public TimeSpan ExtendedSearchTimeout = TimeSpan.FromSeconds(8);

    [DataField, AutoPausedField]
    public TimeSpan SearchDeadline;

    /// <summary>Maximum number of alternate approaches before releasing the job.</summary>
    [DataField]
    public int RepathLimit = 2;

    /// <summary>Shared waypoint arrival tolerance for navigation and steering.</summary>
    [DataField]
    public float ArrivalRange = 0.2f;

    /// <summary>Maximum motion relative to the serviced grid before starting repair.</summary>
    [DataField]
    public float RepairSpeedLimit = 0.1f;

    /// <summary>The station that owns this drone's reserved berth.</summary>
    [DataField]
    public EntityUid? Station;

    [DataField]
    public bool HasWorkingName;

    /// <summary>Per-drone manual recall cooldown, retained across docking, power-off and transfers.</summary>
    [DataField]
    public TimeSpan RecallCooldown = TimeSpan.FromMinutes(5);

    [DataField, AutoPausedField]
    public TimeSpan NextRecall;

    [ViewVariables]
    public ShipRepairDroneCommand Command;

    public bool ReturnBlocked;
    public bool ExitBlocked;

    [ViewVariables]
    public bool Enabled;

    [ViewVariables]
    public EntityUid? Grid;

    /// <summary>Persist with the original masks so saving a phased drone cannot make it permanently intangible.</summary>
    [DataField]
    public bool Phased;

    [ViewVariables]
    public bool WaitingForShip;

    [ViewVariables]
    public int Revision;

    [DataField, AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField, AutoPausedField]
    public TimeSpan NextSearch;

    [DataField, AutoPausedField]
    public TimeSpan ProgressDeadline;

    [DataField, AutoPausedField]
    public TimeSpan NavigationDeadline;

    [DataField, AutoPausedField]
    public TimeSpan SettleTime;

    /// <summary>Limits repeated attempts to find a free place when asked to yield.</summary>
    [DataField, AutoPausedField]
    public TimeSpan NextYield;

    public Vector2 SettlePosition;
    public bool Settling;
    public float BestWaypointDistance = float.PositiveInfinity;
    public int Repaths;
    public Vector2i? WorkTile;
    public bool Yielding;
    public int NavigationRevision = -1;
    public Vector2? FailureOrigin;
    public readonly HashSet<ShipRepairTarget> DeferredSearches = new();
    /// <summary>Eligibility passes for the shared grid stages; reset when changing assignments/grids.</summary>
    public readonly Dictionary<ShipRepairStage, ShipRepairStageProbe> StageProbes = new();
    public int AssignmentWorkRevision;
    public readonly List<EntityUid> ConstructionEffects = new();
    public EntityCoordinates? LastSafePosition;
    public DoAfterId? RepairDoAfter;
    public DoAfterId? PryDoAfter;
    public DoAfterId? ClearDoAfter;
    public EntityUid? ClearTarget;
    public bool ClearForReplacement;
    public Vector2 ClearSettlePosition;
    [AutoPausedField]
    public TimeSpan ClearSettleTime;
    /// <summary>Temporary obstructions reserved until the current job ends.</summary>
    public readonly HashSet<EntityUid> ClearableReservations = new();
    /// <summary>Dismantling is finished, but a hull patch remains until its replacement is ready.</summary>
    public readonly HashSet<EntityUid> PreparedClearables = new();
    /// <summary>Use a walking route when every work position must first be cleared of foam.</summary>
    public bool ClearingRoute;
    /// <summary>The door being opened, so another drone opening it can interrupt our pry.</summary>
    public EntityUid? PryTarget;
    public bool RecoveringDoor;
    [AutoPausedField]
    public TimeSpan DoorRecoveryDeadline;
    [AutoPausedField]
    public TimeSpan NextDoorRecovery;
    public ShipRepairTarget? Target;
    /// <summary>Continue available work on the selected tile/area between successful repair cycles.</summary>
    public Vector2i? FocusTile;
    /// <summary>Chosen fleet work position, rather than stopping within reach of a single batch member.</summary>
    public Vector2? BatchWorkPosition;
    public ShipRepairClosureCheck? ClosureCheck;
    /// <summary>The timed repair has finished; its closure check may still be running.</summary>
    public bool RepairReady;
    /// <summary>Elapsed tool time belongs only to the current, immutable quoted batch.</summary>
    public TimeSpan RepairElapsed;
    [AutoPausedField]
    public TimeSpan RepairStartedAt;
    public TimeSpan RepairTimerDuration;
    /// <summary>Ready work is retained while waiting for a short move or a temporary obstruction.</summary>
    [AutoPausedField]
    public TimeSpan RepairPublishDeadline;
    public ShipRepairPlan? PublicationPlan;
    public bool PublishIndividually;
    public Vector2? RepairReposition;
    [AutoPausedField]
    public TimeSpan RepairRepositionDeadline;
    [AutoPausedField]
    public TimeSpan NextRepairReposition;
    /// <summary>Bounded access audit; an inconclusive closing job yields to other repairs.</summary>
    [AutoPausedField]
    public TimeSpan ClosureDeadline;
    public ShipRepairPlan? Plan;
    public ShipRepairPathSearch? Search;
    public readonly List<Vector2> Path = new();
    public int PathIndex;

    /// <summary>Collision masks to restore on leaving phase, including after a map save/load.</summary>
    [DataField]
    public Dictionary<string, int> SolidMasks = new();

    /// <summary>Transient retry deadlines; pause handling preserves the remaining delay.</summary>
    [AutoPausedField]
    public readonly Dictionary<ShipRepairTarget, TimeSpan> FailedTargets = new();

    /// <summary>Recently failed approaches are not immediately selected again.</summary>
    [AutoPausedField]
    public readonly Dictionary<Vector2i, TimeSpan> FailedPositions = new();
    public int EjectRing = 1;
    public PhysShapeCircle? ClearanceShape;
    public PhysShapeCircle? RecoveryShape;
    public PhysShapeCircle? RecoveryDestinationShape;
}

public enum ShipRepairClearanceState : byte
{
    None,
    Moving,
    WaitingForSpace,
}

public enum ShipRepairNavigationIssue : byte
{
    None,
    InsufficientClearance,
    StartBlocked,
    RecoveryBlocked,
    RecoveryTimedOut,
}

/// <summary>Incremental grid-relative A* state; no work is performed in the component.</summary>
public sealed class ShipRepairPathSearch
{
    public required Box2 Bounds;
    public int Revision;
    public bool ReverseTurn;
    public bool TransientObstruction;
    public readonly HashSet<Vector2i> Goals = new();
    public readonly HashSet<Vector2i> Starts = new();
    public readonly ShipRepairPathFrontier Forward = new();
    public readonly ShipRepairPathFrontier Reverse = new();
}

public sealed class ShipRepairPathFrontier
{
    public readonly PriorityQueue<Vector2i, float> Open = new();
    public readonly Dictionary<Vector2i, float> Costs = new();
    public readonly Dictionary<Vector2i, Vector2i> Previous = new();
    public readonly HashSet<Vector2i> Closed = new();
}
