using Content.Shared._Exodus.Visuals; // Exodus - generic entity link visuals
using Content.Shared.Interaction; // Exodus - bluespace RPED
using Robust.Shared.Audio;
using Robust.Shared.Prototypes; // Exodus - generic entity link visuals

namespace Content.Server._NF.Construction.Components;

[RegisterComponent]
public sealed partial class PartExchangerComponent : Component
{
    /// <summary>
    /// How long it takes to exchange the parts
    /// </summary>
    [DataField("exchangeDuration")]
    public float ExchangeDuration = 3;

    // Exodus-begin - bluespace RPED
    /// <summary>
    /// Whether distance and obstruction checks are required.
    /// Setting this to false bypasses both checks entirely.
    /// </summary>
    // Exodus-end
    [DataField("doDistanceCheck")]
    public bool DoDistanceCheck = true;

    // Exodus-begin - bluespace RPED
    /// <summary>
    /// Maximum distance at which the exchanger can be used.
    /// </summary>
    [DataField]
    public float ExchangeRange = SharedInteractionSystem.InteractionRange;

    /// <summary>
    /// Whether the exchanger uses visual line of sight instead of physical interaction obstruction.
    /// </summary>
    [DataField]
    public bool UseLineOfSight;

    /// <summary>
    /// Whether the exchange is applied immediately without starting a do-after.
    /// </summary>
    [DataField]
    public bool InstantExchange;

    /// <summary>
    /// Whether the exchanger can access machine parts through a closed maintenance panel.
    /// </summary>
    [DataField]
    public bool IgnorePanel;

    /// <summary>
    /// Visual link style shown after a successful exchange. Null disables the effect.
    /// </summary>
    [DataField]
    public ProtoId<EntityLinkVisualPrototype>? ExchangeVisualStyle;

    /// <summary>
    /// How long the successful exchange visual link remains visible.
    /// </summary>
    [DataField]
    public TimeSpan ExchangeVisualDuration = TimeSpan.FromSeconds(1);
    // Exodus-end

    [DataField("exchangeSound")]
    public SoundSpecifier ExchangeSound = new SoundPathSpecifier("/Audio/Items/rped.ogg");

    public EntityUid? AudioStream;
}
