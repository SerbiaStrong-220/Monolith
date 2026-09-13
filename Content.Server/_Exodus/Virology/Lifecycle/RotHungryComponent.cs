using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Persistent hunting, retreat and corpse delivery state, shared by the NPC and ghost role.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotHungryComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Weapons = new();

    [DataField]
    public float SearchRange = 12f;

    /// <summary>Approach path obstacles closely enough to reach their tile with a ground strike.</summary>
    [DataField]
    public float ObstacleRange = 0.8f;

    [DataField]
    public float RetreatDamageFraction = 0.5f;

    [DataField]
    public float ReturnDamageFraction = 0.5f;

    [DataField]
    public TimeSpan RetreatDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan PursuitMemory = TimeSpan.FromSeconds(2);

    /// <summary>Minimum movement towards the retreating creature between observations.</summary>
    [DataField]
    public float PursuitDistance = 0.25f;

    [DataField]
    public float FrenzySpeedMultiplier = 1.2f;

    [DataField]
    public float FrenzyAttackRateMultiplier = 1.25f;

    [DataField]
    public float Regeneration = 5f;

    [DataField]
    public TimeSpan RegenerationDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan StuckDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan StuckAttackInterval = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan DetourDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan DetourDuration = TimeSpan.FromSeconds(15);

    [DataField]
    public float DetourRange = 10f;

    [DataField, AutoPausedField]
    public TimeSpan? ObstructionSince;

    [DataField, AutoPausedField]
    public TimeSpan DetourUntil;

    [DataField]
    public EntityCoordinates? DetourPoint;

    public CancellationTokenSource? DetourCancellation;

    public Task<PathResultEvent>? DetourPath;

    /// <summary>Number of tiles around the occupied tile included in a ground strike.</summary>
    [DataField]
    public int StuckTileRadius = 1;

    [DataField(required: true)]
    public DamageSpecifier StuckDamage = new();

    [DataField]
    public SoundSpecifier? StuckSound;

    [DataField]
    public HashSet<EntityUid> Prey = [];

    [DataField]
    public HashSet<EntityUid> Corpses = [];

    [DataField]
    public EntityUid? Target;

    [DataField]
    public EntityUid? Corpse;

    [DataField]
    public EntityUid? Nest;

    [DataField]
    public EntityCoordinates Home;

    [DataField]
    public EntityCoordinates? Destination;

    [DataField]
    public EntityCoordinates? Shelter;

    [DataField]
    public EntityCoordinates LastPosition;

    [DataField]
    public bool Retreating;

    [DataField]
    public bool Frenzied;

    [DataField]
    public bool ClearingObstacle;

    [DataField]
    public Dictionary<EntityUid, EntityCoordinates> PursuitPositions = new();

    [DataField]
    public EntityUid? Pursuer;

    [DataField, AutoPausedField]
    public TimeSpan RetreatUntil;

    [DataField, AutoPausedField]
    public TimeSpan PursuitUntil;

    [DataField, AutoPausedField]
    public TimeSpan NextThink;

    [DataField, AutoPausedField]
    public TimeSpan NextRegeneration;

    [DataField, AutoPausedField]
    public TimeSpan LastMoved;

    [DataField, AutoPausedField]
    public TimeSpan NextStuckAttack;

    [DataField, AutoPausedField]
    public TimeSpan NextNestSearch;

    [DataField, AutoPausedField]
    public TimeSpan NextShelterSearch;
}
