using Content.Shared.FixedPoint;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(HealOverTimeSystem))]
public sealed partial class HealOverTimeComponent : Component
{
    /// <summary>
    /// Health restored per second.
    /// </summary>
    [DataField]
    public FixedPoint2 HealPerSecond = 10;

    /// <summary>
    /// How often a healing tick is applied.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>When the buff ends.</summary>
    [ViewVariables]
    public TimeSpan EndTime;

    /// <summary>Next tick.</summary>
    [ViewVariables]
    public TimeSpan NextTick;
}
