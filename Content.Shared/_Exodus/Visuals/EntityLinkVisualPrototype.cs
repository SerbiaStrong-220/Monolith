using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Visuals;

/// <summary>
/// Defines how a visual link between two entities is rendered on the client.
/// </summary>
[Prototype("entityLinkVisual")]
public sealed partial class EntityLinkVisualPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Base color shared by both layers of the link.
    /// </summary>
    [DataField]
    public Color Color { get; private set; } = Color.White;

    /// <summary>
    /// Half-width of the outer link layer in world units.
    /// </summary>
    [DataField]
    public float OuterHalfWidth { get; private set; } = 0.12f;

    /// <summary>
    /// Half-width of the inner link layer in world units.
    /// </summary>
    [DataField]
    public float InnerHalfWidth { get; private set; } = 0.035f;

    /// <summary>
    /// Alpha multiplier for the outer link layer.
    /// </summary>
    [DataField]
    public float OuterAlpha { get; private set; } = 0.25f;

    /// <summary>
    /// Alpha multiplier for the inner link layer.
    /// </summary>
    [DataField]
    public float InnerAlpha { get; private set; } = 0.9f;

    /// <summary>
    /// Time over which a temporary link fades before it expires.
    /// </summary>
    [DataField]
    public TimeSpan FadeDuration { get; private set; } = TimeSpan.FromSeconds(0.25);
}
