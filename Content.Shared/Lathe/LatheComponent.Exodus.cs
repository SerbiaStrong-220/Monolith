// Exodus: configurable output position for machines larger than one tile.
using System.Numerics;

namespace Content.Shared.Lathe;

public sealed partial class LatheComponent
{
    /// <summary>Local offset of printed items, rotated with the machine.</summary>
    [DataField]
    public Vector2 OutputOffset;
}
