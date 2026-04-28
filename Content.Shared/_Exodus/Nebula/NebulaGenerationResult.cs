namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Result of pure Exodus nebula generation.
/// </summary>
public sealed class NebulaGenerationResult
{
    public readonly List<NebulaShape> Nebulas = new();
    public NebulaGenerationRejections Rejections;
    public int Attempts;
    public int RequestedCount;

    public bool Complete => Nebulas.Count >= RequestedCount;
}
