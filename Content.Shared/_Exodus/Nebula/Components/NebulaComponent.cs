using Content.Shared._Exodus.Nebula.Generation;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Sits on a generated nebula marker entity. Identifies the nebula in its map's list and
/// exposes the underlying shape; per-effect components on the same entity describe what
/// happens to things in this nebula.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaComponent : Component
{
    [ViewVariables]
    public int Index;

    [ViewVariables]
    public NebulaShape Shape;
}
