using Content.Server._Exodus.Shuttles.Systems;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// Present on an <see cref="FtlSuppressorComponent"/> entity while its field is projected (anchored and powered).
/// Lets the suppression checks iterate only working suppressors.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(FtlSuppressorSystem))]
public sealed partial class ActiveFtlSuppressorComponent : Component
{
    /// <summary>
    /// Next time the field position is compared with <see cref="PublishedPosition"/>.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextZoneRefresh;

    /// <summary>
    /// Field position that was last sent to shuttle consoles.
    /// </summary>
    [ViewVariables]
    public MapCoordinates PublishedPosition = MapCoordinates.Nullspace;
}
