// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Robust.Shared.GameStates;

namespace Content.Shared.Exodus.Gimmicks.Pheromones;

[RegisterComponent, NetworkedComponent]
public sealed partial class PheromonesComponent : Component
{
    [DataField]
    public Color Color = Color.Yellow;

    [DataField]
    public string Text;

    /// <summary>
    /// Whether or not this entity can be seen by those who cant read pheromones
    /// </summary>
    [DataField]
    public bool Hidden = false;

    [DataField]
    public Color? OldSpriteColor;
}
