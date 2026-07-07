using System.Numerics;
using Content.Shared.Humanoid;
using Robust.Shared.GameStates;

namespace Content.Shared.Body.Part;

public sealed partial class BodyPartComponent
{
    /// <summary>
    /// Overrides the humanoid visual layer inferred from the part type and symmetry.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HumanoidVisualLayers? VisualLayerOverride;

    /// <summary>
    /// Offset applied to sprites of items held by the hand represented by this part.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 InhandVisualOffset;
}
