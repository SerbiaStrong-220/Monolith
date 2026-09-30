using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Stealth;

/// <summary>Settings for a handheld pulse that reveals cloaked entities, including those inside containers.</summary>
[RegisterComponent]
public sealed partial class StealthDisruptorComponent : Component
{
    /// <summary>Pulse radius in world units.</summary>
    [DataField] public float Range = 10f;

    /// <summary>How long revealed targets are prevented from cloaking again.</summary>
    [DataField] public TimeSpan SuppressionDuration = TimeSpan.FromSeconds(20);

    /// <summary>When enabled, blockers between the user and the target's outer container stop the pulse.</summary>
    [DataField] public bool RequiresLineOfSight;

    /// <summary>Effect attached to a revealed entity or its outermost container.</summary>
    [DataField] public EntProtoId RevealEffect = "StealthRevealEffect";

    /// <summary>Positional detection sound, emitted once per revealed outer container or loose target.</summary>
    [DataField] public SoundSpecifier RevealSound = new SoundPathSpecifier("/Audio/Effects/chime.ogg");
}
