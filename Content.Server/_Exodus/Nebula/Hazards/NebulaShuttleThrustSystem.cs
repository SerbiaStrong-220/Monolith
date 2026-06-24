using Content.Server._Exodus.Nebula.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Events;
using Content.Shared._Exodus.Nebula.Hazards;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Hazards;

/// <summary>
/// Applies thrust reduction to shuttles inside nebulas with
/// <see cref="NebulaThrustReductionComponent"/>. Effective thrust is cached per shuttle because
/// <see cref="GetNebulaShuttleThrustEvent"/> is raised from MoverController during active
/// movement, while nebula/thruster inputs change comparatively rarely.
/// </summary>
public sealed partial class NebulaShuttleThrustSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;

    private EntityQuery<ThrusterComponent> _thrusterQuery;
    private EntityQuery<NebulaThrustMultiplierComponent> _thrusterMultiplierQuery;
    private EntityQuery<NebulaThrustResistanceComponent> _resistanceQuery;

    /// <summary>Marker prototype id → thrust multiplier. Rebuilt on prototype reload.</summary>
    private readonly Dictionary<string, float> _multiplierByMarker = new();

    public override void Initialize()
    {
        base.Initialize();

        _thrusterQuery = GetEntityQuery<ThrusterComponent>();
        _thrusterMultiplierQuery = GetEntityQuery<NebulaThrustMultiplierComponent>();
        _resistanceQuery = GetEntityQuery<NebulaThrustResistanceComponent>();

        SubscribeLocalEvent<GetNebulaShuttleThrustEvent>(OnGetNebulaShuttleThrust);
        SubscribeLocalEvent<NebulaPresenceChangedEvent>(OnPresenceChanged);
        SubscribeLocalEvent<ShuttleComponent, ShuttleLinearThrustChangedEvent>(OnShuttleLinearThrustChanged);
        SubscribeLocalEvent<NebulaThrustMultiplierComponent, ComponentStartup>(OnThrusterNebulaModifierChanged);
        SubscribeLocalEvent<NebulaThrustMultiplierComponent, ComponentShutdown>(OnThrusterNebulaModifierChanged);
        SubscribeLocalEvent<NebulaThrustResistanceComponent, ComponentStartup>(OnThrusterNebulaResistanceChanged);
        SubscribeLocalEvent<NebulaThrustResistanceComponent, ComponentShutdown>(OnThrusterNebulaResistanceChanged);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildCache();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
        {
            BuildCache();
            DirtyAllShuttleCaches();
        }
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
        if (!TryComp<ShuttleComponent>(args.ShuttleUid, out var shuttle) ||
            !TryGetCurrentThrustMultiplier(args.ShuttleUid, out var multiplier))
        {
            return;
        }

        var cache = EnsureComp<NebulaShuttleThrustCacheComponent>(args.ShuttleUid);
        if (cache.Dirty)
            RebuildCache(args.ShuttleUid, shuttle, cache, multiplier);

        args.HorizontalThrust = GetCachedDirectionThrust(cache, args.HorizontalDirectionIndex, args.HorizontalThrust);
        args.VerticalThrust = GetCachedDirectionThrust(cache, args.VerticalDirectionIndex, args.VerticalThrust);
    }

    private void OnPresenceChanged(ref NebulaPresenceChangedEvent args)
    {
        DirtyCache(args.Entity);
    }

    private void OnShuttleLinearThrustChanged(Entity<ShuttleComponent> ent, ref ShuttleLinearThrustChangedEvent args)
    {
        DirtyCache(ent.Owner);
    }

    private void OnThrusterNebulaModifierChanged(Entity<NebulaThrustMultiplierComponent> ent, ref ComponentStartup args)
    {
        DirtyThrusterGridCache(ent.Owner);
    }

    private void OnThrusterNebulaModifierChanged(Entity<NebulaThrustMultiplierComponent> ent, ref ComponentShutdown args)
    {
        DirtyThrusterGridCache(ent.Owner);
    }

    private void OnThrusterNebulaResistanceChanged(Entity<NebulaThrustResistanceComponent> ent, ref ComponentStartup args)
    {
        DirtyThrusterGridCache(ent.Owner);
    }

    private void OnThrusterNebulaResistanceChanged(Entity<NebulaThrustResistanceComponent> ent, ref ComponentShutdown args)
    {
        DirtyThrusterGridCache(ent.Owner);
    }

    private void DirtyThrusterGridCache(EntityUid thrusterUid)
    {
        var xform = Transform(thrusterUid);
        if (xform.GridUid is { Valid: true } gridUid)
            DirtyCache(gridUid);
    }

    private void DirtyCache(EntityUid shuttleUid)
    {
        if (TryComp<NebulaShuttleThrustCacheComponent>(shuttleUid, out var cache))
            cache.Dirty = true;
    }

    private void DirtyAllShuttleCaches()
    {
        var query = EntityQueryEnumerator<NebulaShuttleThrustCacheComponent>();
        while (query.MoveNext(out _, out var cache))
        {
            cache.Dirty = true;
        }
    }

    private void RebuildCache(
        EntityUid shuttleUid,
        ShuttleComponent shuttle,
        NebulaShuttleThrustCacheComponent cache,
        float multiplier)
    {
        for (var i = 0; i < cache.EffectiveLinearThrust.Length; i++)
        {
            cache.EffectiveLinearThrust[i] = GetEffectiveDirectionThrust(
                shuttleUid,
                i,
                shuttle.LinearThrust[i],
                multiplier,
                true);
        }

        cache.Dirty = false;
    }

    private static float GetCachedDirectionThrust(NebulaShuttleThrustCacheComponent cache, int directionIndex, float fallbackThrust)
    {
        if ((uint) directionIndex >= cache.EffectiveLinearThrust.Length)
            return fallbackThrust;

        return cache.EffectiveLinearThrust[directionIndex];
    }

    public float GetCurrentThrustMultiplier(EntityUid shuttleUid)
    {
        TryGetCurrentThrustMultiplier(shuttleUid, out var multiplier);
        return multiplier;
    }

    public bool TryGetCurrentThrustMultiplier(EntityUid shuttleUid, out float multiplier)
    {
        if (_multiplierByMarker.Count == 0)
        {
            multiplier = 1f;
            return HasComp<NebulaPresenceComponent>(shuttleUid);
        }

        if (!TryComp<NebulaPresenceComponent>(shuttleUid, out var presence))
        {
            multiplier = 1f;
            return false;
        }

        if (presence.Marker.Id is not { } id ||
            !_multiplierByMarker.TryGetValue(id, out multiplier))
        {
            multiplier = 1f;
        }

        return true;
    }

    public float GetEffectiveDirectionThrust(EntityUid shuttleUid, int directionIndex, float fallbackThrust)
    {
        var inNebula = TryGetCurrentThrustMultiplier(shuttleUid, out var multiplier);
        return GetEffectiveDirectionThrust(
            shuttleUid,
            directionIndex,
            fallbackThrust,
            multiplier,
            inNebula);
    }

    public float GetEffectiveDirectionThrust(EntityUid shuttleUid, int directionIndex, float fallbackThrust, float nebulaMultiplier)
    {
        return GetEffectiveDirectionThrust(
            shuttleUid,
            directionIndex,
            fallbackThrust,
            nebulaMultiplier,
            HasComp<NebulaPresenceComponent>(shuttleUid));
    }

    public float GetEffectiveDirectionThrust(
        EntityUid shuttleUid,
        int directionIndex,
        float fallbackThrust,
        float nebulaMultiplier,
        bool inNebula)
    {
        if (!inNebula || fallbackThrust <= 0f)
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
            effectiveThrust += GetEffectiveThrusterThrust(thrusterUid, thruster.Thrust, nebulaMultiplier, inNebula);
        }

        var remainingThrust = fallbackThrust - accountedThrust;
        if (remainingThrust != 0f)
            effectiveThrust += remainingThrust * nebulaMultiplier;

        return MathF.Max(0f, effectiveThrust);
    }

    public float GetEffectiveThrusterThrust(EntityUid thrusterUid, float thrust, float nebulaMultiplier)
    {
        return GetEffectiveThrusterThrust(thrusterUid, thrust, nebulaMultiplier, true);
    }

    public float GetEffectiveThrusterThrust(EntityUid thrusterUid, float thrust, float nebulaMultiplier, bool inNebula)
    {
        if (!inNebula || thrust <= 0f)
            return thrust;

        var resistance = GetThrustReductionResistance(thrusterUid);
        // Direct nebula multiplier is applied before slowdown resistance by design.
        var scaledThrust = thrust * GetNebulaThrustMultiplier(thrusterUid);
        return scaledThrust * GetEffectiveMultiplier(nebulaMultiplier, resistance);
    }

    public float GetNebulaThrustMultiplier(EntityUid thrusterUid)
    {
        if (!_thrusterMultiplierQuery.TryComp(thrusterUid, out var multiplier))
            return 1f;

        return MathF.Max(0f, multiplier.Multiplier);
    }

    public float GetThrustReductionResistance(EntityUid thrusterUid)
    {
        if (!_resistanceQuery.TryComp(thrusterUid, out var resistance))
            return 0f;

        return MathF.Max(0f, resistance.Resistance);
    }

    private static float GetEffectiveMultiplier(float nebulaMultiplier, float resistance)
    {
        // Resistance above 1 is intentional: in slowing nebulas it converts the ignored slowdown
        // into extra thrust. Example: multiplier 0.5 and resistance 2 gives multiplier 1.5.
        return 1f - (1f - nebulaMultiplier) * (1f - resistance);
    }
}
