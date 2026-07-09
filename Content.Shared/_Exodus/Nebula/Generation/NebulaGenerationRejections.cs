namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Rejection counters for debugging Exodus nebula generation.
/// </summary>
public record struct NebulaGenerationRejections
{
    public int InvalidSettings;
    public int InvalidShape;
    public int OutOfBounds;
    public int ProtectedArea;
    public int Overlap;
    public int AreaLimit;
}
