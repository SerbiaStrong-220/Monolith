using Content.Shared._Exodus.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Movement.Components;
using Robust.Client.Player;

namespace Content.Client._Exodus.Atmos;

public sealed class GasTankFillIndicatorSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    private EntityQuery<GasTankFillIndicatorComponent> _indicatorQuery;
    private EntityQuery<GasTankComponent> _tankQuery;
    private EntityQuery<JetpackComponent> _jetpackQuery;
    private EntityQuery<ActiveJetpackComponent> _activeJetpackQuery;

    public override void Initialize()
    {
        base.Initialize();

        _indicatorQuery = GetEntityQuery<GasTankFillIndicatorComponent>();
        _tankQuery = GetEntityQuery<GasTankComponent>();
        _jetpackQuery = GetEntityQuery<JetpackComponent>();
        _activeJetpackQuery = GetEntityQuery<ActiveJetpackComponent>();
    }

    /// <summary>
    /// Gets the indicator for a tank currently used by the local player.
    /// </summary>
    public bool TryGetFill(EntityUid uid, out byte level, out bool lowPressure)
    {
        level = 0;
        lowPressure = false;

        if (_player.LocalEntity is not { } user ||
            !_indicatorQuery.TryComp(uid, out var indicator) ||
            !_tankQuery.TryComp(uid, out var tank))
        {
            return false;
        }

        var breathing = tank.User == user;
        var flying = _activeJetpackQuery.HasComp(uid) &&
            _jetpackQuery.TryComp(uid, out var jetpack) && jetpack.JetpackUser == user;

        if (!breathing && !flying)
            return false;

        level = indicator.FillLevel;
        lowPressure = breathing && indicator.IsLowPressure;
        return true;
    }
}
