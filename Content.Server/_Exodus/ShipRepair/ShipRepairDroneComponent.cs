using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.DeviceLinking;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;

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

    /// <summary>Empty space included outside the saved/current hull for exterior navigation.</summary>
    [DataField]
    public float ExteriorMargin = 8f;

    /// <summary>Maximum relative ship speed when the drone is outside its assigned grid.</summary>
    [DataField]
    public float MaximumShipSpeed = 3f;

    /// <summary>Navigation clearance, slightly larger than the 0.25 tile body radius.</summary>
    [DataField]
    public float Clearance = 0.28f;

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

    [DataField]
    public ProtoId<SinkPortPrototype> OnPort = "On";

    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";

    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

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
    public readonly List<EntityUid> ConstructionEffects = new();
    public EntityCoordinates? LastSafePosition;
    public DoAfterId? RepairDoAfter;
    public DoAfterId? PryDoAfter;
    public ShipRepairTarget? Target;
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
    public readonly HashSet<EntityUid> BlockedDoors = new();
    public int EjectRing = 1;
    public PhysShapeCircle? ClearanceShape;
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
