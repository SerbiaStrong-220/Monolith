using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotDefenderComponent : Component
{
    [DataField]
    public bool RepathBeforeStrike;

    [DataField]
    public bool AvoidThreatsOnRoutes;

    [DataField]
    public bool RepathedObstruction;

    [DataField]
    public float SearchRange = 32f;

    [DataField]
    public float EscapeRange = 18f;

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan RestDuration = TimeSpan.FromSeconds(14);

    [DataField]
    public HashSet<EntityUid> Enemies = [];

    [DataField]
    public EntityUid? Target;

    [DataField]
    public EntityCoordinates? EscapePoint;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RestUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan MoveUntil;

    [DataField]
    public TimeSpan ColonyRestDuration = TimeSpan.FromSeconds(12);

    [DataField]
    public TimeSpan StuckDelay = TimeSpan.FromSeconds(2.5);

    [DataField]
    public TimeSpan DetourDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan StuckAttackInterval = TimeSpan.FromSeconds(1.5);

    [DataField]
    public int StuckTileRadius = 1;

    [DataField(required: true)]
    public DamageSpecifier StuckDamage = new();

    [DataField]
    public SoundSpecifier? StuckSound;

    [DataField]
    public RotDefenderRoute Route;

    [DataField]
    public bool RouteFailed;

    [DataField]
    public EntityUid? DetouredTarget;

    [DataField]
    public EntityUid? Threat;

    [DataField]
    public bool DamagePending;

    [DataField]
    public bool EnvironmentalDamage;

    [DataField]
    public HashSet<EntityUid> UnreachableEnemies = [];

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RetryEnemiesAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastMoved;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? ObstructionSince;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextStuckAttack;

    [DataField]
    public EntityCoordinates LastPosition;

    [DataField]
    public bool MovementRequested;

    [DataField]
    public bool ClearingObstacle;

    public CancellationTokenSource? PathCancellation;

    public Task<PathResultEvent>? EscapePath;
}

public enum RotDefenderRoute : byte
{
    None,
    Wander,
    Cover,
    Colony,
    Detour,
}
