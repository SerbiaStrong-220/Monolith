namespace Content.Server._Exodus.Worldgen.Components;

/// <summary>
/// Prevents a player-controlled entity from loading new worldgen chunks around itself.
/// </summary>
[RegisterComponent]
public sealed partial class WorldChunkLoadingExemptComponent : Component
{
    /// <summary>
    /// Keeps already loaded worldgen chunks nearby from unloading without loading new chunks.
    /// </summary>
    [DataField]
    public bool RetainLoadedChunks;

    /// <summary>
    /// Radius in worldgen chunks within which loaded chunks are protected from unloading.
    /// </summary>
    [DataField]
    public int RetainLoadedChunksRadius = 2;

    /// <summary>
    /// Ensures the worldgen chunk owning the grid underneath the entity is loaded.
    /// </summary>
    [DataField]
    public bool EnsureParentDebrisChunkLoaded;

    /// <summary>
    /// Ensures chunks owning grids underneath tail segments are loaded.
    /// </summary>
    [DataField]
    public bool EnsureTailDebrisChunksLoaded;
}
