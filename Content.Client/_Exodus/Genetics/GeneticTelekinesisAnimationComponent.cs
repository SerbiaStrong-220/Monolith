using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._Exodus.Genetics;

/// <summary>Short-lived local sprite offset; never changes the authoritative item position.</summary>
[RegisterComponent]
public sealed partial class GeneticTelekinesisAnimationComponent : Component
{
    public EntityCoordinates Start;
    public EntityCoordinates End;
    public TimeSpan Duration;
    public TimeSpan Expires;
    public TimeSpan? Started;
    public Vector2 OriginalOffset;
}
