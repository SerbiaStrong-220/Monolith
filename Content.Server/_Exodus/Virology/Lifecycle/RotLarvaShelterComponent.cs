using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotLarvaShelterComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RetryAt;

    public CancellationTokenSource? Cancellation;
    public Task<PathResultEvent>? Path;
    public EntityCoordinates? Destination;
}
