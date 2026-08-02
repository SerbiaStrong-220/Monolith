using Content.Shared.Interaction; // Exodus - bluespace RPED
using Robust.Shared.Audio;

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
    /// Color of the beam shown after a successful exchange. Null disables the beam.
    /// </summary>
    [DataField]
    public Color? ExchangeBeamColor;

    /// <summary>
    /// How long the successful exchange beam remains visible.
    /// </summary>
    [DataField]
    public TimeSpan ExchangeBeamDuration = TimeSpan.FromSeconds(1);
    // Exodus-end

    [DataField("exchangeSound")]
    public SoundSpecifier ExchangeSound = new SoundPathSpecifier("/Audio/Items/rped.ogg");

    public EntityUid? AudioStream;
}
