namespace Content.Server.Radio.Components;

/// <summary>
///     This component allows an entity to directly translate radio messages into chat messages. Note that this does not
///     automatically add an <see cref="ActiveRadioComponent"/>, which is required to receive radio messages on specific
///     channels.
/// </summary>
[RegisterComponent]
public sealed partial class IntrinsicRadioReceiverComponent : Component
{
    //SS220 Silicon TTS fix begin
    /// <summary>
    ///     Optional entity that will act as a place to play radio messages
    ///     (e.g. AI eye instead of AI core).
    /// </summary>
    public EntityUid? ReceiverEntityOverride { get; set; }
    //SS220 Silicon TTS fix end
}
