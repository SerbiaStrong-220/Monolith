namespace Content.Server.Radio;

public readonly partial record struct RadioReceiveEvent
{
    /// <summary>
    /// Characters already sent this chat message, even when several headsets or an intrinsic radio receive it.
    /// Shared across every receiver of this transmission; independent of the TTS receiver list.
    /// </summary>
    public HashSet<EntityUid> ChatRecipients { get; } = new();

    /// <summary>
    /// Characters whose headset receive event has fired, independently of intrinsic chat reception.
    /// Keeps headset-specific listeners working when an intrinsic radio delivers the chat first.
    /// </summary>
    public HashSet<EntityUid> HeadsetRecipients { get; } = new();
}
