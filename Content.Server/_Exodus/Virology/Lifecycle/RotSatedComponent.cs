using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotSatedComponent : Component
{
    [DataField]
    public float SearchRange = 32f;

    [DataField]
    public float ThreatRange = 8f;

    [DataField]
    public float EscapeRange = 18f;

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan RestDuration = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan StripInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan ConsumeDuration = TimeSpan.FromMinutes(1);

    [DataField]
    public TimeSpan EnvelopDuration = TimeSpan.FromSeconds(1.2);

    [DataField]
    public TimeSpan RiseDuration = TimeSpan.FromSeconds(1.2);

    [DataField]
    public TimeSpan BirthDuration = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan SpillInterval = TimeSpan.FromSeconds(10);

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> SlurryReagent;

    [DataField]
    public FixedPoint2 SlurryAmount = 10;

    [DataField(required: true)]
    public EntProtoId Larva;

    [DataField]
    public int LarvaePerCorpse = 4;

    [DataField]
    public SoundSpecifier? ConsumeSound;

    [DataField]
    public string EnvelopState = "envelop";

    [DataField]
    public string ConsumeState = "consume";

    [DataField]
    public string RiseState = "rise";

    [DataField]
    public string BirthState = "brood";

    [DataField]
    public HashSet<EntityUid> Enemies = [];

    [DataField]
    public EntityUid? Target;

    [DataField]
    public EntityUid? Corpse;

    [DataField]
    public EntityUid? UnreachableCorpse;

    [DataField]
    public DoAfterId? ConsumeDoAfter;

    [DataField]
    public RotSatedActivity Activity;

    [DataField]
    public int PendingLarvae;

    [DataField]
    public bool BirthRequested;

    [DataField]
    public EntityCoordinates? EscapePoint;

    [DataField]
    public bool Escaping;

    [DataField, AutoPausedField]
    public TimeSpan NextThink;

    [DataField, AutoPausedField]
    public TimeSpan NextStrip;

    [DataField, AutoPausedField]
    public TimeSpan NextSpill;

    [DataField, AutoPausedField]
    public TimeSpan ActivityUntil;

    [DataField, AutoPausedField]
    public TimeSpan RestUntil;

    [DataField, AutoPausedField]
    public TimeSpan MoveUntil;

    [DataField, AutoPausedField]
    public TimeSpan CorpseRetryAt;

    public CancellationTokenSource? PathCancellation;

    public Task<PathResultEvent>? EscapePath;
}

public enum RotSatedActivity : byte
{
    None,
    Enveloping,
    Consuming,
    Rising,
    Birthing,
}
