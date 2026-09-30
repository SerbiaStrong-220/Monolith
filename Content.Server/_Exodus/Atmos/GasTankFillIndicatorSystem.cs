using Content.Shared._Exodus.Atmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Atmos;

public sealed partial class GasTankFillIndicatorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private EntityQuery<GasTankFillIndicatorComponent> _indicatorQuery;
    private EntityQuery<GasTankComponent> _tankQuery;

    // One schedule for sampling active consumers, independent of individual tanks.
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        _indicatorQuery = GetEntityQuery<GasTankFillIndicatorComponent>();
        _tankQuery = GetEntityQuery<GasTankComponent>();
        _nextUpdate = _timing.CurTime;

        SubscribeLocalEvent<GasTankFillIndicatorComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<GasTankFillIndicatorComponent> ent, ref MapInitEvent args)
    {
        UpdateIndicator(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate += UpdateInterval;
        if (_nextUpdate < now)
            _nextUpdate = now + UpdateInterval;

        // Only player-controlled internals need a HUD indicator.
        var internalsQuery = EntityQueryEnumerator<ActorComponent, InternalsComponent>();
        while (internalsQuery.MoveNext(out _, out _, out var internals))
        {
            UpdateIndicator(internals.GasTankEntity);
        }

        var jetpackQuery = EntityQueryEnumerator<ActiveJetpackComponent>();
        while (jetpackQuery.MoveNext(out var uid, out _))
        {
            UpdateIndicator(uid);
        }
    }

    private void UpdateIndicator(EntityUid? uid)
    {
        if (!_indicatorQuery.TryComp(uid, out var indicator) ||
            !_tankQuery.TryComp(uid, out var tank) ||
            TerminatingOrDeleted(uid.Value))
        {
            return;
        }

        var nominalMoles = indicator.NominalMoles ??
            indicator.NominalPressure * tank.Air.Volume / (Atmospherics.R * Atmospherics.T20C);
        var currentMoles = tank.Air.TotalMoles;

        // Keep the scale fixed while consuming gas, but expand it after filling above nominal.
        // Otherwise the bar stays full until the supply falls below the nominal amount.
        if (float.IsFinite(currentMoles))
            indicator.MaxObservedMoles = Math.Max(indicator.MaxObservedMoles, currentMoles);

        var fullMoles = Math.Max(nominalMoles, indicator.MaxObservedMoles);

        // Use a fixed reference temperature so heating a tank cannot refill its indicator.
        var fraction = fullMoles > 0f && float.IsFinite(fullMoles)
            ? currentMoles / fullMoles
            : 0f;
        var level = float.IsFinite(fraction)
            ? (byte) Math.Clamp(MathF.Ceiling(fraction * 100f), 0f, 100f)
            : (byte) 0;
        var lowPressure = tank.IsLowPressure;

        if (indicator.FillLevel == level && indicator.IsLowPressure == lowPressure)
            return;

        indicator.FillLevel = level;
        indicator.IsLowPressure = lowPressure;
        Dirty(new Entity<GasTankFillIndicatorComponent>(uid.Value, indicator));
    }
}
