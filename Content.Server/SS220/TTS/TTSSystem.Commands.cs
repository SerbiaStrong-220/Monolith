// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.SS220.TTS.Commands;

namespace Content.Server.SS220.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem
{
    public void RequestResetAllClientQueues()
    {
        var ev = new TtsQueueResetMessage();
        RaiseNetworkEvent(ev);
    }
}
