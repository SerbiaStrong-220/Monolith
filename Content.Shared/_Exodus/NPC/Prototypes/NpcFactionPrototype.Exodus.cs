namespace Content.Shared.NPC.Prototypes;

public sealed partial class NpcFactionPrototype
{
    /// <summary>
    /// Optional player-facing faction name used by Exodus radar labels.
    /// </summary>
    [DataField]
    public LocId? Name { get; private set; }

    /// <summary>
    /// Optional faction name form used by Exodus AI core control radar labels.
    /// </summary>
    [DataField]
    public LocId? CoreControlName { get; private set; }
}
