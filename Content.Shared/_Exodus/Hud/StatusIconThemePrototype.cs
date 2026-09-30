using Content.Shared.Alert;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Hud;

/// <summary>
/// Appearance of status alerts and the body damage indicator.
/// </summary>
[Prototype]
public sealed partial class StatusIconThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localized name shown in the interface settings.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; }

    /// <summary>
    /// Display order in the theme selector.
    /// </summary>
    [DataField]
    public int Order { get; private set; }

    /// <summary>
    /// Optional alert icons, ordered from the alert's minimum severity upwards.
    /// Alerts without an override keep their original icons.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<AlertPrototype>, List<SpriteSpecifier>> AlertIcons { get; private set; } = new();

    /// <summary>
    /// Directory containing the body part RSI files used by the damage indicator.
    /// Must end with a slash so resource validation treats it as a directory.
    /// </summary>
    [DataField(required: true)]
    public ResPath PartStatusPath { get; private set; }
}
