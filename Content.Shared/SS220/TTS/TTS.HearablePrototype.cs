// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Prototypes;

namespace Content.Shared.SS220.TTS;

public interface IHearableChannelPrototype : IPrototype
{
    string ID { get; }
    string LocalizedName { get; }
    Color Color { get; }
}
