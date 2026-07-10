using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Prototypes;

/// <summary>
/// Marks a fixed world-space circle as protected from nebula generation. Used for hard-coded
/// safe spots like the map origin.
/// </summary>
[Prototype("nebulaProtectedPoint")]
public sealed partial class NebulaProtectedPointPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = null!;

    [DataField]
    public Vector2 Position { get; private set; } = Vector2.Zero;

    /// <summary>
    /// Radius in world tiles. If zero, falls back to <see cref="NebulaGenerationSettings.ProtectedRadius"/>.
    /// </summary>
    [DataField]
    public float Radius { get; private set; }
}
