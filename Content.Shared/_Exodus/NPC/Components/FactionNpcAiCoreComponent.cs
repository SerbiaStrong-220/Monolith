using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.NPC.Components;

/// <summary>
/// Marks an NPC ship AI core that can visibly control its grid on mass scanners.
/// </summary>
[RegisterComponent]
public sealed partial class FactionNpcAiCoreComponent : Component
{
    /// <summary>
    /// The faction represented by this core for radar labels.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> Faction;

    /// <summary>
    /// Additional factions this core treats as friendly targets for ship AI targeting.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> IgnoredFactions = new();

    /// <summary>
    /// Runtime grid currently marked by this core.
    /// </summary>
    [ViewVariables]
    public EntityUid? CurrentControlGrid;
}
