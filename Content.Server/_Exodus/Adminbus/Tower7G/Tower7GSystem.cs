// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._Exodus.Adminbus.Tower7G;

public sealed partial class Tower7GSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private float _updateTimer = 0;
    private const float UpdateTimer = 5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;

        if (_updateTimer < UpdateTimer)
            return;

        _updateTimer = 0;

        var query = EntityQueryEnumerator<Tower7GComponent>();
        while (query.MoveNext(out var uid, out var tower))
        {
            var xform = Transform(uid);
            var pos = _transform.GetWorldPosition(xform);
            var targets = _lookup.GetEntitiesInRange<Tower7GTargetComponent>(xform.Coordinates, tower.Range);

            foreach (var target in targets)
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
