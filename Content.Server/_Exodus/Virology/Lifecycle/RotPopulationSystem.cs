using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>
/// Bounds automatic rot reproduction per grid, so unattended nests and nurseries cannot snowball into server load.
/// Counts are refreshed periodically and reservations keep several producers in one interval under the cap.
/// </summary>
public sealed partial class RotPopulationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CountInterval = TimeSpan.FromSeconds(2);
    private readonly Dictionary<EntityUid, int> _counts = [];
    private TimeSpan _nextCount;
    private int _cap;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, EXCVars.RotPopulationCap, value => _cap = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCount)
            return;
        _nextCount = _timing.CurTime + CountInterval;
        Recount();
    }

    /// <summary>Recounts living rot creatures immediately, e.g. after admin cleanup.</summary>
    public void Recount()
    {
        _counts.Clear();
        var query = EntityQueryEnumerator<RotCreatureComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var mob, out var xform))
        {
            if (mob.CurrentState == MobState.Dead || GetArea(xform) is not { } area)
                continue;
            _counts[area] = _counts.GetValueOrDefault(area) + 1;
        }
    }

    public bool IsCrowded(EntityUid uid, int amount = 1)
    {
        if (_cap <= 0 || GetArea(Transform(uid)) is not { } area)
            return false;
        return _counts.GetValueOrDefault(area) + amount > _cap;
    }

    /// <summary>Claims room for new creatures near the entity. Returns false when its area is full.</summary>
    public bool TryReserve(EntityUid uid, int amount = 1)
    {
        if (_cap <= 0 || GetArea(Transform(uid)) is not { } area)
            return true;
        var count = _counts.GetValueOrDefault(area);
        if (count + amount > _cap)
            return false;
        _counts[area] = count + amount;
        return true;
    }

    private static EntityUid? GetArea(TransformComponent xform) => xform.GridUid ?? xform.MapUid;
}
