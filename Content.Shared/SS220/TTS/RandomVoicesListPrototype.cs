// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Prototypes;

namespace Content.Shared.SS220.TTS;

/// <summary>
/// Prototype that contains a list of voices for randomize
/// </summary>
[Prototype("randomVoicesList")]
public sealed partial class RandomVoicesListPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// List of TTSVoicePrototype
    /// </summary>
    [DataField("voices")]
    public IReadOnlyList<string> VoicesList { get; private set; } = new List<string>();
}
