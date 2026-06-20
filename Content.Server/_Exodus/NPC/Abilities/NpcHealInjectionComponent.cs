using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcHealAbilitiesSystem))]
public sealed partial class NpcHealInjectionComponent : Component
{
    /// <summary>Action granted for this ability.</summary>
    [DataField]
    public EntProtoId Action = "ActionHealInjectionNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Ability cooldown.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    /// <summary>Health per second granted to the target.</summary>
    [DataField]
    public FixedPoint2 HealPerSecond = 10;

    /// <summary>How long the injected heal-over-time lasts.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(5);

    /// <summary>Sound played on use.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
