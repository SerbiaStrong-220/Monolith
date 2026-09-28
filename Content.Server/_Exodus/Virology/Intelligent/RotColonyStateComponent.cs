using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Intelligent;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotColonyStateComponent : Component
{
    [DataField] public HashSet<EntityUid> Members = [];
    [DataField] public HashSet<EntityUid> Projects = [];
    [DataField] public EntityUid? Grid;
    [DataField] public EntityUid? PilotGrid;
    [DataField] public EntityUid? RallyMarker;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextIncome;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextVision;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextUi;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan RallyUntil;
    [DataField] public EntityCoordinates? Rally;
    [DataField] public string? Feedback;
    [ViewVariables] public bool DirtyNetwork = true;

    /// <summary>Another cut can skip the full disconnect pass until a supported member reconnects.</summary>
    [ViewVariables] public bool SupportDisconnected;

    [ViewVariables] public uint Revision;
    /// <summary>Occupied cells and whether any occupying member conducts the colony connection.</summary>
    public Dictionary<Vector2i, bool> Cells = [];
    public Dictionary<Vector2i, bool> BuildingCells = [];
    public HashSet<EntityUid>.Enumerator NetworkEnumerator;
    public RotNetworkPhase NetworkPhase;
    public float NetworkIncome;
    public bool RefreshPending;
    public HashSet<Vector2i> WatchedCells = [];
    public HashSet<Vector2i> BuildingWatchedCells = [];
    public readonly Dictionary<Vector2i, EntityUid> Reservations = [];
    public HashSet<Vector2i> Connected = [];
    public HashSet<Vector2i> BuildingConnected = [];
    public readonly Queue<Vector2i> Frontier = [];
    public readonly HashSet<Vector2i> Seen = [];
    public readonly List<EntityUid> Scratch = [];
    [AutoPausedField] public readonly Dictionary<EntityUid, TimeSpan> Alerts = [];
    public readonly HashSet<Vector2i> LastView = [];
    public EntityUid? ViewFrame;
    public Vector2i? ViewCenter;
    public readonly HashSet<RotVisionStamp> VisionInputs = [];
    public bool ProjectsRestored;
    public DoAfterId? Rooting;
}

public readonly record struct RotVisionStamp(EntityUid Source, Box2 Bounds, float Range);

public enum RotNetworkPhase : byte
{
    Idle,
    Index,
    Flood,
    Apply,
    Refresh,
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotConstructionComponent : Component
{
    [DataField] public EntityUid Core;
    [DataField] public EntityUid? Target;
    [DataField] public ProtoId<RotBuildingPrototype>? Building;
    [DataField] public RotProjectKind Kind;
    [DataField] public Vector2i Tile;
    [DataField] public Vector2i Size = Vector2i.One;
    [DataField] public int Rotation;
    [DataField] public float Reserved;
    [DataField] public float OriginalDamage;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Started;
    [DataField] public TimeSpan Duration;
    [DataField] public DoAfterId? DoAfter;
    public bool Settled;
    [DataField] public bool Ready;
    [DataField] public bool WaitingForNetwork;
}

public enum RotProjectKind : byte
{
    Build,
    Repair,
    Dissolve,
}
