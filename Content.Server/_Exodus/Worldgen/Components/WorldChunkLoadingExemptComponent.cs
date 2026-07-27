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
}
