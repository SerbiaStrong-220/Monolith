using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Result of pure Exodus nebula generation.
/// </summary>
public sealed class NebulaGenerationResult
{
    public readonly List<NebulaShape> Nebulas = new();
    public readonly List<EntProtoId> NebulaPrototypes = new();
    public NebulaGenerationRejections Rejections;
    public int Attempts;
    public int MaxAttempts;
    public double MaxTotalArea;
    public double TotalArea;
    public bool HitAreaLimit;

    public bool Complete => HitAreaLimit || Attempts < MaxAttempts;
}
