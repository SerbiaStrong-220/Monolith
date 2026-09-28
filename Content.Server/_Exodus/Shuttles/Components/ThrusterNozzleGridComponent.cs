using Content.Server.Shuttles.Systems;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>Anchored nozzle engines to reconsider when this grid's tiles change.</summary>
[RegisterComponent, Access(typeof(ThrusterSystem))]
public sealed partial class ThrusterNozzleGridComponent : Component
{
    public readonly HashSet<EntityUid> Thrusters = new();
}
