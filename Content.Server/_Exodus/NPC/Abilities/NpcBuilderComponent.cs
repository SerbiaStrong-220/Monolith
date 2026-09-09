using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.Abilities;

[RegisterComponent]
public sealed partial class NpcBuilderComponent : Component
{
    /// <summary>Action granted for this ability (HTN uses this id).</summary>
    [DataField]
    public EntProtoId Action = "ActionBuildNPC";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>What is build.</summary>
    [DataField(required: true)]
    public EntProtoId Build;

    /// <summary>How long build takes.</summary>
    [DataField]
    public TimeSpan BuildTime = TimeSpan.FromSeconds(10);

    /// <summary>Cd between builds.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>Up to how many structures mob will constuct.</summary>
    [DataField]
    public int Limit = 1;

    /// <summary>For tracking structures this mob has built.</summary>
    [ViewVariables]
    public List<EntityUid> Built = new();
}
