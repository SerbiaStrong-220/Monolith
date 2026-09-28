using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Whitelist;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Response to armed structures, independent of humanoid or infection target selection.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotRetaliationComponent : Component
{
    [DataField]
    public EntityWhitelist Attackers = new() { Components = ["Gun"] };

    [DataField]
    public TimeSpan ReachTimeout = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan RetreatDuration = TimeSpan.FromSeconds(14);

    [DataField]
    public float EscapeRange = 30f;

    [DataField]
    public float AdvanceRange = 3f;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public bool Retreating;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReachBy;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan RetreatUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPathSearch;

    [DataField]
    public EntityCoordinates? Destination;

    [DataField]
    public Vector2 Direction;

    public CancellationTokenSource? PathCancellation;
    public Task<PathResultEvent>? Path;
}
