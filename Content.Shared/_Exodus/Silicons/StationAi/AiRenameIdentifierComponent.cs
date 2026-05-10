using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Silicons.StationAi;

/// <summary>
/// Stores the parenthesized identifier suffix of an AI core name separately from the editable base name,
/// so the user can rename the core without losing the identifier and without relying on string parsing.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AiRenameIdentifierComponent : Component
{
    [DataField]
    public string Identifier = string.Empty;
}
