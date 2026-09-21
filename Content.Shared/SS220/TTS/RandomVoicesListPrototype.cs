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
    public IReadOnlyList<ProtoId<TTSVoicePrototype>> VoicesList { get; private set; } = []; // Exodus: validate voice references.

    // Exodus-begin: optional sex matching for humanoid voice pools.
    /// <summary>
    /// Only choose voices matching the humanoid's sex. Unsexed humanoids may use the whole pool.
    /// An existing voice is retained if it belongs to the pool and matches the humanoid's sex.
    /// </summary>
    [DataField]
    public bool MatchSex { get; private set; }
    // Exodus-end
}
