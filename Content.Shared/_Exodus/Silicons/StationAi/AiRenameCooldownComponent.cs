using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Silicons.StationAi;

/// <summary>
/// Tracks when the held AI shell is allowed to rename its core again.
/// Lives on the StationAiHeld entity so it gets deleted with the round.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AiRenameCooldownComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan NextRenameAt;
}
