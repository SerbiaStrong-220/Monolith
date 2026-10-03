using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Nutrition;

/// <summary>
/// Keeps a waiting body's physiological reserves until its first mind arrives.
/// Activation persists when that mind leaves, including ghosting and disconnection.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(NeedsActivationSystem))]
public sealed partial class NeedsActivationComponent : Component
{
    /// <summary>Whether this body has already started consuming its reserves.</summary>
    [DataField, AutoNetworkedField]
    public bool Activated;
}
