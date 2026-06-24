using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Data-driven territory settings for POI grids.
/// Profiles choose matching POI game map prototypes centrally, so maps do not carry territory balance values.
/// </summary>
[Prototype]
public sealed partial class TerritoryProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Used when a GridTerritory game map / station id is not listed by another profile.
    /// </summary>
    [DataField]
    public bool Default { get; private set; }

    /// <summary>
    /// Game map prototypes that use this profile.
    /// </summary>
    [DataField]
    public List<ProtoId<GameMapPrototype>> GameMapPrototypes { get; private set; } = new();

    /// <summary>
    /// Territory influence radius in world units.
    /// </summary>
    [DataField(required: true)]
    public float Radius { get; private set; }

    /// <summary>
    /// Entity prototype spawned at the territory center to provide biome/ambient selection.
    /// Null disables biome source spawning for this territory profile.
    /// </summary>
    [DataField]
    public ProtoId<EntityPrototype>? BiomeSourcePrototype { get; private set; }

    /// <summary>
    /// If true, territory banners can claim this territory.
    /// Display-only service or hub territories should keep their marker and biome behavior while disabling claims.
    /// </summary>
    [DataField]
    public bool Claimable { get; private set; } = true;

    /// <summary>
    /// Minimum fraction of snapshotted SRD-repairable entities that must still be anchored on this grid
    /// before a territory banner can be anchored to claim it.
    /// If unset, territoryClaimRules.defaultMinClaimRepairIntegrity is used.
    /// Zero disables the integrity gate and avoids scanning the SRD snapshot.
    /// </summary>
    [DataField]
    public float? MinClaimRepairIntegrity { get; private set; }

    /// <summary>
    /// If true, POI icon/label colors on shuttle maps follow territory ownership.
    /// </summary>
    [DataField]
    public bool ColorPoiByFaction { get; private set; } = true;

    /// <summary>
    /// POI icon/label color while the territory has no controlling faction.
    /// </summary>
    [DataField]
    public Color NeutralPoiColor { get; private set; } = new(0.65f, 0.65f, 0.65f);
}
