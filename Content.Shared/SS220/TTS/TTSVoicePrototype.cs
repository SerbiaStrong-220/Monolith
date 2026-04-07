// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Shared.SS220.TTS;

/// <summary>
/// Prototype represent available TTS voices
/// </summary>
[Prototype("ttsVoice")]
// ReSharper disable once InconsistentNaming
public sealed partial class TTSVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("sex", required: true)]
    public Sex Sex { get; private set; } = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("speaker", required: true)]
    public string Speaker { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the species is available "at round start" (In the character editor)
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; private set; } = true;

    [DataField("sponsorOnly")]
    public bool SponsorOnly { get; private set; } = false;
}
