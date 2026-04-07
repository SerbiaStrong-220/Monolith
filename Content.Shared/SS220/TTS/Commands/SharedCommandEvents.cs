// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Serialization;

namespace Content.Shared.SS220.TTS.Commands;

[Serializable, NetSerializable]
public sealed class TtsQueueResetMessage : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class SessionSendTTSMessage(bool value) : EntityEventArgs
{
    public bool Value { get; init; } = value;
}
