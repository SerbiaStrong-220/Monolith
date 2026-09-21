using Content.Server._Exodus.ShipRepair;
using Content.Server.Shuttles.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private bool IsRepairDroneIgnoringThrusters(EntityUid uid)
    {
        return HasComp<ShipRepairDroneComponent>(uid);
    }

    private bool IsActiveThruster(EntityUid uid)
    {
        return TryComp<ThrusterComponent>(uid, out var thruster) && thruster.IsOn;
    }
}
