using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcHealAbilitiesSystem))]
public sealed partial class NpcReviveComponent : Component
{
    /// <summary>Main revive action (have cooldown).</summary>
    /// This is for actions that have target only.
    [DataField]
    public EntProtoId Action = "ActionReviveNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Recharge of the main revive.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(120);

    /// <summary>Field-defib drag variant (used if the main revive is recharging).</summary>
    [DataField]
    public EntProtoId DragAction = "ActionReviveDragNPC";

    [DataField]
    public EntityUid? DragActionEntity;

    /// <summary>Recharge of the field-defib (zero for now, tested, funny).</summary>
    [DataField]
    public TimeSpan DragCooldown = TimeSpan.Zero;

    /// <summary>Health restored in one jolt.</summary>
    [DataField]
    public FixedPoint2 Heal = 120;

    /// <summary>Jitters duration after the jolt.</summary>
    [DataField]
    public TimeSpan JitterDuration = TimeSpan.FromSeconds(10);

    /// <summary>Sound played at the moment of discharge.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/Defib/defib_zap.ogg");
}
