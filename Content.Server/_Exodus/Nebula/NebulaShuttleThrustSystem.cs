using Content.Shared._Exodus.Nebula;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Applies thrust reduction to shuttles inside nebulas with
/// <see cref="NebulaThrustReductionComponent"/>. Cache-driven because
/// <see cref="GetNebulaShuttleThrustEvent"/> is raised from MoverController on every physics
/// step — resolving the marker prototype and its component each time would burn lookups.
/// </summary>
public sealed class NebulaShuttleThrustSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    /// <summary>Marker prototype id → thrust multiplier. Rebuilt on prototype reload.</summary>
    private readonly Dictionary<string, float> _multiplierByMarker = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetNebulaShuttleThrustEvent>(OnGetNebulaShuttleThrust);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildCache();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            BuildCache();
    }

    private void BuildCache()
    {
        _multiplierByMarker.Clear();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<NebulaComponent>(out _, _componentFactory))
                continue;

            if (proto.TryGetComponent<NebulaThrustReductionComponent>(out var comp, _componentFactory))
                _multiplierByMarker[proto.ID] = comp.Multiplier;
        }
    }

    private void OnGetNebulaShuttleThrust(ref GetNebulaShuttleThrustEvent args)
    {
        if (_multiplierByMarker.Count == 0)
            return;

        if (!TryComp<NebulaPresenceComponent>(args.ShuttleUid, out var presence))
            return;

        if (presence.Marker.Id is not { } id ||
            !_multiplierByMarker.TryGetValue(id, out var multiplier))
        {
            return;
        }

        args.HorizontalThrust *= multiplier;
        args.VerticalThrust *= multiplier;
    }
}
