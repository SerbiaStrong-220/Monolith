using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcSelfHealSystem))]
public sealed partial class NpcSelfHealComponent : Component
{
    /// <summary>Action granted for this ability.</summary>
    /// This is for actions that target self only
    [DataField]
    public EntProtoId Action = "ActionSelfHealNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Cooldown.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>How long the buff lasts.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(5);

    /// <summary>Total health restored over the duration.</summary>
    [DataField]
    public FixedPoint2 TotalHeal = 50;

    /// <summary>How often the heal ticks.</summary>
    [DataField]
    public TimeSpan HealInterval = TimeSpan.FromSeconds(1);

    /// <summary>Walk speed multiplier while buffed.</summary>
    [DataField]
    public float WalkSpeedModifier = 0.6f;

    /// <summary>Sprint speed multiplier while buffed.</summary>
    [DataField]
    public float SprintSpeedModifier = 0.6f;

    /// <summary>Looped sound for the duration.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/Medical/brutepack_begin.ogg");

    // Runtime points below

    /// <summary>Buff end point. Null if inactive.</summary>
    [ViewVariables]
    public TimeSpan? EndTime;

    [ViewVariables]
    public TimeSpan NextHeal;

    [ViewVariables]
    public FixedPoint2 PerTick;

    [ViewVariables]
    public FixedPoint2 Healed;

    [ViewVariables]
    public EntityUid? SoundEntity;
}
