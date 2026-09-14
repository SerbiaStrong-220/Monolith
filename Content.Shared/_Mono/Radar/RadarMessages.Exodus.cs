// Exodus: replicate capture deadlines in the radar palette, including territories outside the viewer's PVS.
namespace Content.Shared._Mono.Radar;

public partial record struct BlipConfig
{
    /// <summary>End of an active territory capture, or null for ordinary radar labels.</summary>
    [DataField]
    public TimeSpan? CaptureEndsAt = null;
}
