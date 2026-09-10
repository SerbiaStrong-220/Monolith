using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.NameModifier;

/// <summary>
/// A localized prefix kept separate from the entity's editable base name.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EntityNamePrefixComponent : Component
{
    /// <summary>
    /// Localization key for the prefix, without the character's name.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public LocId Prefix = string.Empty;
}
