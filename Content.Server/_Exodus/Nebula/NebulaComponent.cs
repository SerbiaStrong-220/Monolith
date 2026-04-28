using Content.Shared._Exodus.Nebula;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Invisible runtime marker for a generated Exodus space nebula.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaComponent : Component
{
    [ViewVariables]
    public int Index;

    [ViewVariables]
    public NebulaShape Shape;
}
