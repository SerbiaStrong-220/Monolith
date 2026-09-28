using Content.Shared.DoAfter;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>A functional organ's bounded growth frontier. Walls, doors and tissue never receive this component.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotSpreadComponent : Component
{
    [DataField] public int Radius = 5;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(5);
    [DataField] public TimeSpan ConversionDuration = TimeSpan.FromSeconds(4);
    [DataField] public float ConversionSecondsPerDamage = 0.015f;
    [DataField] public TimeSpan AttackInterval = TimeSpan.FromSeconds(5);
    [DataField]
    public DamageSpecifier ObstacleDamage = new()
    {
        DamageDict = new() { ["Blunt"] = 25, ["Caustic"] = 5 },
    };
    [DataField] public EntProtoId Tissue = "RotTissue";
    [DataField] public EntProtoId Wall = "RotWall";
    [DataField] public EntProtoId Door = "RotOrganicDoor";
    [DataField] public EntProtoId Marker = "RotConstructionMarker";
    [DataField] public EntityWhitelist ConvertibleWalls = new() { Tags = new() { "Wall" } };
    [DataField] public EntityWhitelist Excluded = new() { Components = new[] { "Docking" }, Tags = new() { "SpreaderIgnore" } };
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextGrowth;
    [DataField] public EntityUid? PendingMarker;
    public EntityUid? Grid;
    public Box2i Bounds;
    public Vector2i Origin;
    public Vector2i Size;
    public int Rotation;
    public bool Rebuild = true;
    public bool Active;
    public readonly Queue<Vector2i> Frontier = [];
    public readonly HashSet<Vector2i> Seen = [];
    public readonly List<Vector2i> Watched = [];
    public EntityUid? Target;
    public Vector2i TargetTile;
    public DoAfterId? Conversion;
    public bool ConversionReady;
}

/// <summary>Local subscriptions avoid scanning other grids or all organs when a tile changes.</summary>
[RegisterComponent]
public sealed partial class RotSpreadGridComponent : Component
{
    public readonly Dictionary<Vector2i, HashSet<EntityUid>> Watchers = [];
    public readonly Dictionary<Vector2i, EntityUid> Reservations = [];
}
