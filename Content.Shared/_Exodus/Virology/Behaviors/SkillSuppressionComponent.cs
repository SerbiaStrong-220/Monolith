// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SkillSuppressionComponent : Component
{
    /// <summary>Exodus has no skill trees; cognitive impairment slows timed actions instead.</summary>
    [DataField, AutoNetworkedField]
    public float DelayMultiplier = 1.5f;
}
