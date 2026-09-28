using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotSatedComponent : Component
{
    [DataField]
    public float ThreatRange = 8f;

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

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextThink;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextActivityUpdate;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSpill;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ActivityUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CorpseRetryAt;

    [DataField]
    public EntProtoId ConsumeAction = "ActionRotSatedConsume";

    [DataField]
    public EntProtoId StopAction = "ActionRotSatedStop";

    [DataField]
    public EntProtoId StrikeAction = "ActionRotSatedStrike";

    [DataField]
    public EntityUid? ConsumeActionEntity;

    [DataField]
    public EntityUid? StopActionEntity;

    [DataField]
    public EntityUid? StrikeActionEntity;

    [DataField]
    public DoAfterId? StripDoAfter;

    [DataField]
    public bool PreparingConsumption;
}

public enum RotSatedActivity : byte
{
    None,
    Enveloping,
    Consuming,
    Rising,
    Birthing,
}
