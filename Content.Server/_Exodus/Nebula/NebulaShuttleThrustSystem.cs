using Content.Server.Shuttles.Components;
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

    private EntityQuery<ThrusterComponent> _thrusterQuery;
    private EntityQuery<NebulaThrustResistanceComponent> _resistanceQuery;

    /// <summary>Marker prototype id → thrust multiplier. Rebuilt on prototype reload.</summary>
    private readonly Dictionary<string, float> _multiplierByMarker = new();

    public override void Initialize()
    {
        base.Initialize();

        _thrusterQuery = GetEntityQuery<ThrusterComponent>();
        _resistanceQuery = GetEntityQuery<NebulaThrustResistanceComponent>();

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
        var multiplier = GetCurrentThrustMultiplier(args.ShuttleUid);

        args.HorizontalThrust = GetEffectiveDirectionThrust(
            args.ShuttleUid,
            args.HorizontalDirectionIndex,
            args.HorizontalThrust,
            multiplier);
        args.VerticalThrust = GetEffectiveDirectionThrust(
            args.ShuttleUid,
            args.VerticalDirectionIndex,
            args.VerticalThrust,
            multiplier);
    }

    public float GetCurrentThrustMultiplier(EntityUid shuttleUid)
    {
        if (_multiplierByMarker.Count == 0)
            return 1f;

        if (!TryComp<NebulaPresenceComponent>(shuttleUid, out var presence))
            return 1f;

        if (presence.Marker.Id is not { } id ||
            !_multiplierByMarker.TryGetValue(id, out var multiplier))
        {
            return 1f;
        }

        return multiplier;
    }

    public float GetEffectiveDirectionThrust(EntityUid shuttleUid, int directionIndex, float fallbackThrust)
    {
        return GetEffectiveDirectionThrust(
            shuttleUid,
            directionIndex,
            fallbackThrust,
            GetCurrentThrustMultiplier(shuttleUid));
    }

    public float GetEffectiveDirectionThrust(EntityUid shuttleUid, int directionIndex, float fallbackThrust, float nebulaMultiplier)
    {
        if (nebulaMultiplier == 1f || fallbackThrust <= 0f)
            return fallbackThrust;

        if (!TryComp<ShuttleComponent>(shuttleUid, out var shuttle) ||
            (uint)directionIndex >= shuttle.LinearThrusters.Length)
        {
            return fallbackThrust * nebulaMultiplier;
        }

        var thrusters = shuttle.LinearThrusters[directionIndex];
        if (thrusters.Count == 0)
            return fallbackThrust * nebulaMultiplier;

        var accountedThrust = 0f;
        var effectiveThrust = 0f;

        for (var i = 0; i < thrusters.Count; i++)
        {
            var thrusterUid = thrusters[i];
            if (!_thrusterQuery.TryComp(thrusterUid, out var thruster))
                continue;

            accountedThrust += thruster.Thrust;
            effectiveThrust += GetEffectiveThrusterThrust(thrusterUid, thruster.Thrust, nebulaMultiplier);
        }

        var remainingThrust = fallbackThrust - accountedThrust;
        if (remainingThrust != 0f)
            effectiveThrust += remainingThrust * nebulaMultiplier;

        return MathF.Max(0f, effectiveThrust);
    }

    public float GetEffectiveThrusterThrust(EntityUid thrusterUid, float thrust, float nebulaMultiplier)
    {
        var resistance = GetThrustReductionResistance(thrusterUid);
        return thrust * GetEffectiveMultiplier(nebulaMultiplier, resistance);
    }

    public float GetThrustReductionResistance(EntityUid thrusterUid)
    {
        if (!_resistanceQuery.TryComp(thrusterUid, out var resistance))
            return 0f;

        return Math.Clamp(resistance.Resistance, 0f, 1f);
    }

    private static float GetEffectiveMultiplier(float nebulaMultiplier, float resistance)
    {
        return 1f - (1f - nebulaMultiplier) * (1f - resistance);
    }
}
