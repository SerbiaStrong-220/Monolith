using Content.Shared.NPC.Prototypes;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Company;

public sealed partial class CompanyPrototype
{
    /// <summary>
    /// Optional sprite shown for this company in the character lobby.
    /// </summary>
    [DataField]
    public SpriteSpecifier? LobbyImage { get; private set; }

    /// <summary>
    /// Current company that replaces this legacy identity. Must point directly to a company without a successor.
    /// </summary>
    [DataField]
    public ProtoId<CompanyPrototype>? Successor { get; private set; }

    /// <summary>
    /// Whether this company appears in the diplomacy directory, independently of character selection.
    /// Companies must explicitly opt in through their prototype.
    /// </summary>
    [DataField]
    public bool DiplomacyVisible { get; private set; }

    /// <summary>
    /// Optional membership icon. Its visibility whitelist defines who can recognize company members.
    /// </summary>
    [DataField]
    public ProtoId<FactionIconPrototype>? StatusIcon { get; private set; }

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
