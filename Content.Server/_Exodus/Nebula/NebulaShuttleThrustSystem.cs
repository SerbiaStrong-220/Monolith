using Content.Shared._Exodus.Nebula;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaShuttleThrustSystem : EntitySystem
{
    private const float ThrustMultiplier = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetNebulaShuttleThrustEvent>(OnGetNebulaShuttleThrust);
    }

    private void OnGetNebulaShuttleThrust(ref GetNebulaShuttleThrustEvent args)
    {
        if (!TryComp<NebulaPresenceComponent>(args.ShuttleUid, out var presence) ||
            presence.Type is not (NebulaType.Green or NebulaType.Purple))
        {
            return;
        }

        args.HorizontalThrust *= ThrustMultiplier;
        args.VerticalThrust *= ThrustMultiplier;
    }
}
