using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Silicons.StationAi;

/// <summary>
/// Stores the player-chosen base name of the AI shell (without the trailing identifier suffix).
/// Lives on the StationAiBrain so the custom name survives Intellicard transfers — the brain
/// persists across core re-insertions, while the core name gets overwritten by upstream on each insert.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AiRenameBaseNameComponent : Component
{
    [DataField]
    public string BaseName = string.Empty;
}
