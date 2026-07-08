using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Global territory claim tuning. Faction-specific values live on TerritoryFactionPrototype.
/// </summary>
[Prototype("territoryClaimRules")]
public sealed partial class TerritoryClaimRulesPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Time after round start before runtime banner claims are allowed.
    /// This only affects player-runtime anchoring attempts; mapped anchored banners can still initialize ownership.
    /// </summary>
    [DataField]
    public TimeSpan RoundStartClaimCooldown { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Fallback cooldown before the same faction can claim another territory.
    /// A territoryFaction.claimCooldown value overrides this for that faction.
    /// </summary>
    [DataField]
    public TimeSpan DefaultFactionClaimCooldown { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Fallback SRD repair integrity required for profiles that do not override it.
    /// Zero disables the integrity gate and avoids scanning the SRD snapshot.
    /// </summary>
    [DataField]
    public float DefaultMinClaimRepairIntegrity { get; private set; } = 0f;
}
