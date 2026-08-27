using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent, Access(typeof(NpcDashSystem))]
public sealed partial class NpcDashComponent : Component
{
    /// <summary>Action proto granted for this ability.</summary>
    [DataField]
    public EntProtoId Action = "ActionDashNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>Recharge between dashes.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);
}
