namespace Content.Shared._Exodus.Silicons.StationAi;

/// <summary>
/// Stores the editable and fixed parts of a custom Station AI name.
/// </summary>
[RegisterComponent]
public sealed partial class AiRenameNameComponent : Component
{
    [DataField]
    public string BaseName = string.Empty;

    [DataField]
    public string Identifier = string.Empty;
}
