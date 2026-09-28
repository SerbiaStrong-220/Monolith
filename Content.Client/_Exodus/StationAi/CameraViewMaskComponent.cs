namespace Content.Client._Exodus.StationAi;

/// <summary>Server-authorized tiles for a private camera view rendered by the station AI overlay.</summary>
[RegisterComponent]
public sealed partial class CameraViewMaskComponent : Component
{
    public EntityUid? Frame;
    public readonly HashSet<Vector2i> Tiles = [];
}
