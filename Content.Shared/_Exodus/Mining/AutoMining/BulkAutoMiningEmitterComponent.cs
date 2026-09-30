using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Mining.AutoMining;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
[Access(typeof(SharedBulkAutoMiningSystem))]
public sealed partial class BulkAutoMiningEmitterComponent : Component
{
    /// <summary>Optional localized short name for the console; the entity keeps its full name.</summary>
    [DataField]
    public LocId? ConsoleName;

    /// <summary>Damage to the emitter when firing at a grid without a generated natural deposit.</summary>
    [DataField]
    public DamageSpecifier ForbiddenTileDamage = new();

    [DataField]
    public ProtoId<MaterialPrototype> SlurryMaterial = "MiningSlurry";

    /// <summary>Inclusive range of base material volume per intact tile, before the warmup bonus.</summary>
    [DataField]
    public MinMax SlurryPerTile = new(200, 200);

    /// <summary>Time spent firing to warm up from zero to full output bonus.</summary>
    [DataField]
    public TimeSpan WarmupTime = TimeSpan.FromSeconds(60);

    /// <summary>Time spent idle to cool down from full warmup to zero.</summary>
    [DataField]
    public TimeSpan CooldownTime = TimeSpan.FromSeconds(30);

    /// <summary>Additional slurry yield at full warmup, as a fraction of the base yield.</summary>
    [DataField]
    public double MaxWarmupYieldBonus = 0.35;

    /// <summary>Warmup fraction at the last beam start or stop. Current warmup is calculated on demand.</summary>
    [ViewVariables]
    public double WarmupProgress;

    /// <summary>Pause-aware timestamp of the warmup snapshot; persists between mining jobs.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan WarmupLastUpdate;

    /// <summary>Distance from the head pivot to the forward lens, in world units.</summary>
    [DataField]
    public float MuzzleOffset = 0.75f;

    [DataField]
    public SoundSpecifier? StartSound;

    [ViewVariables]
    public EntityUid? StartupStream;

    [ViewVariables]
    public EntityUid? Controller;

    /// <summary>Persists between jobs so restarting or changing consoles cannot accelerate excavation.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextMiningTime;

    /// <summary>Bounds failed target searches independently of the other lasers' work cycles.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextTargetSearchTime;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? BeamGrid;

    [ViewVariables, AutoNetworkedField]
    public Vector2i BeamTile;

    /// <summary>Partner laser of a consortium link. A linked laser fires at its partner and cannot mine.</summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkPartner;

    /// <summary>Grid of the partner laser; drawn from grid transforms even when the partner is outside PVS.</summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkGrid;

    /// <summary>Pivot of the partner laser in <see cref="LinkGrid"/> coordinates. Anchored lasers never move on their grid.</summary>
    [ViewVariables, AutoNetworkedField]
    public Vector2 LinkPosition;

    /// <summary>Maximum distance between linked lasers, the shorter range of the two consoles that made the link.</summary>
    [ViewVariables]
    public float LinkRange;

    /// <summary>Consecutive failed line-of-sight checks of the current link.</summary>
    [ViewVariables]
    public int LinkObstructedChecks;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextLinkCheck;

    /// <summary>Interval between validity checks of a consortium link.</summary>
    [DataField]
    public TimeSpan LinkCheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Consecutive obstructed checks tolerated before a link breaks, so a passing body only flickers it.</summary>
    [DataField]
    public int LinkObstructionTolerance = 1;

    /// <summary>Why the last link of this laser broke, for admins inspecting it; not networked.</summary>
    [ViewVariables]
    public BulkMiningLinkBreakReason? LastLinkBreak;
}
