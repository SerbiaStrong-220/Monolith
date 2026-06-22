using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcHealAbilitiesSystem))]
public sealed partial class NpcHealAuraComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionHealAuraNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Recharge between auras.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Aura radius in tiles (diameter is twice this).
    /// </summary>
    [DataField]
    public float Radius = 5f;

    /// <summary>
    /// Health per second healed to each friendly creature inside the aura.
    /// </summary>
    [DataField]
    public FixedPoint2 HealPerSecond = 5;

    /// <summary>
    /// How long the aura lasts.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the aura heals.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Visual effect spawned (attached to the caster, so it follows) for the duration.
    /// This should be changed to its own sprite...someday
    /// </summary>
    [DataField]
    public EntProtoId Effect = "EffectHealAuraNPC";

    // Runtime points below

    /// <summary>When the aura ends. Null if inactive.</summary>
    [ViewVariables]
    public TimeSpan? EndTime;

    /// <summary>Next aura heal point.</summary>
    [ViewVariables]
    public TimeSpan NextTick;

    /// <summary>The spawned visual, so it can be removed when the aura ends.</summary>
    [ViewVariables]
    public EntityUid? VisualEntity;
}
