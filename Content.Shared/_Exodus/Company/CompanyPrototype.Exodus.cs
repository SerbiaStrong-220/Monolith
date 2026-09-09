using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Company;

public sealed partial class CompanyPrototype
{
    /// <summary>
    /// Fraction of cash withheld when a company member deposits it through an ATM.
    /// </summary>
    [DataField]
    public float AtmDepositCommission { get; private set; } = 0f;

    /// <summary>
    /// Optional NPC faction applied to players spawned with this company selected.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? NpcFaction { get; private set; }

    // Exodus-begin company-fleet
    /// <summary>
    /// Entity prototypes granted once on player spawn after company assignment
    /// (tech disks, starter kits, etc.). Empty = nothing extra.
    /// </summary>
    [DataField]
    public List<EntProtoId> StartingItems { get; private set; } = new();
    // Exodus-end
}
