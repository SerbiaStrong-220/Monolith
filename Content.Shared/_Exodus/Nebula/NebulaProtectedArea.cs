using System.Numerics;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Circular area that Exodus nebula generation must not touch.
/// </summary>
public readonly record struct NebulaProtectedArea(Vector2 Position, float Radius);
