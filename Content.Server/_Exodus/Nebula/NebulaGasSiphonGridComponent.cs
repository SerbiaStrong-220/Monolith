using System.Collections.Generic;
using Robust.Shared.Maths;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Server-side index of nebula gas siphons with working filters on a grid.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaGasSiphonGridComponent : Component
{
    public const int ClearanceBucketSize = 4;

    public readonly HashSet<EntityUid> Siphons = new();
    public readonly Dictionary<Vector2i, HashSet<EntityUid>> ClearanceBuckets = new();
}
