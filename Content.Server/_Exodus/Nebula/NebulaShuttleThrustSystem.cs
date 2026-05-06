using Content.Shared._Exodus.Nebula;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaShuttleThrustSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetNebulaShuttleThrustEvent>(OnGetNebulaShuttleThrust);
    }

    private void OnGetNebulaShuttleThrust(ref GetNebulaShuttleThrustEvent args)
    {
        if (!TryComp<NebulaPresenceComponent>(args.ShuttleUid, out var presence) ||
            !TryGetThrustReduction(presence.Marker, out var multiplier))
        {
            return;
        }

        args.HorizontalThrust *= multiplier;
        args.VerticalThrust *= multiplier;
    }

    private bool TryGetThrustReduction(EntProtoId marker, out float multiplier)
    {
        multiplier = 1f;

        if (string.IsNullOrEmpty(marker.Id))
            return false;

        if (!_prototype.TryIndex<EntityPrototype>(marker, out var prototype))
            return false;

        if (!prototype.TryGetComponent<NebulaThrustReductionComponent>(out var component, _componentFactory))
            return false;

        multiplier = component.Multiplier;
        return true;
    }
}
