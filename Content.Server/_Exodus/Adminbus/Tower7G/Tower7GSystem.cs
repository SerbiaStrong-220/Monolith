// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Server.Popups;
using Content.Shared._Exodus.Adminbus.Tower7G;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Adminbus.Tower7G;

public sealed partial class Tower7GSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);
    private readonly HashSet<Entity<Tower7GTargetComponent>> _targets = new();

    // Heat and suppression share one sampling schedule, independent of individual targets.
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        _nextUpdate = _timing.CurTime + UpdateInterval;
        SubscribeLocalEvent<Tower7GDamageConversionComponent, MapInitEvent>(OnConversionMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate += UpdateInterval;
        if (_nextUpdate <= now)
            _nextUpdate = now + UpdateInterval;

        UpdateDamageConversion();

        var query = EntityQueryEnumerator<Tower7GComponent>();
        while (query.MoveNext(out var uid, out var tower))
        {
            var xform = Transform(uid);
            var pos = _transform.GetWorldPosition(xform);
            _targets.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, tower.Range, _targets);

            foreach (var target in _targets)
            {
                if (_mobState.IsDead(target))
                    continue;

                var targetPos = _transform.GetWorldPosition(target);
                var dir = pos - targetPos;
                var length = dir.Length();

                var damage = tower.BaseDamage * (tower.MinDamage + length * (tower.MaxDamage - tower.MinDamage) / tower.Range);
                _damageable.TryChangeDamage(target, damage, true);
                _popup.PopupEntity(Loc.GetString(tower.TargetPopup), target, target, PopupType.SmallCaution);
            }
        }
    }
}
