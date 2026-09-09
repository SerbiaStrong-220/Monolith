namespace Content.Server._Exodus.Worldgen;

/// <summary>Generation identity, independent of the grid's player-visible name.</summary>
[RegisterComponent]
public sealed partial class PoiSpawnIdentityComponent : Component
{
    [ViewVariables]
    public PoiSpawnKey Key;

    [ViewVariables]
    public float Clearance;

    /// <summary>Index in the nebula candidate list, or null for ordinary POIs.</summary>
    [ViewVariables]
    public int? NebulaCandidate;
}

/// <summary>Runtime lookup key; prototype references in configuration remain typed.</summary>
public readonly record struct PoiSpawnKey(bool IsNebula, string Id);
