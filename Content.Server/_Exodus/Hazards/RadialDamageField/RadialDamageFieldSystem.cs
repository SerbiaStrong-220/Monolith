// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Hazards.RadialDamageField;

/// <summary>
/// Processes a small set of explicitly marked targets against configured radial damage fields.
/// </summary>
public sealed partial class RadialDamageFieldSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var targets = EntityQueryEnumerator<RadialDamageFieldTargetComponent, DamageableComponent, TransformComponent>();
        while (targets.MoveNext(out var targetUid, out var target, out var damageable, out var targetXform))
        {
            if (targetXform.MapID == MapId.Nullspace || _mobState.IsDead(targetUid))
                continue;

            var targetPosition = _transform.GetWorldPosition(targetXform);
            var fields = EntityQueryEnumerator<RadialDamageFieldComponent, TransformComponent>();
            while (fields.MoveNext(out var fieldUid, out var field, out var fieldXform))
            {
                if (fieldXform.MapID != targetXform.MapID ||
                    !_prototype.TryIndex(field.Profile, out var profile) ||
                    !ProfilesMatch(target.Profiles, profile.TargetProfiles))
                {
                    continue;
                }

                var range = field.RangeOverride ?? profile.Range;
                if (range <= 0f)
                    continue;

                var delta = targetPosition - _transform.GetWorldPosition(fieldXform);
                if (delta.LengthSquared() > range * range)
                    continue;

                _damageable.TryChangeDamage(
                    targetUid,
                    profile.Damage,
                    profile.IgnoreResistances,
                    damageable: damageable,
                    origin: fieldUid);

                if (profile.TargetPopup is { } popup)
                    _popup.PopupEntity(Loc.GetString(popup), targetUid, targetUid, PopupType.LargeCaution);

                // Overlapping fields should not multiply the damage from a single pulse.
                break;
            }
        }
    }

    private static bool ProfilesMatch(HashSet<string> targetProfiles, HashSet<string> fieldProfiles)
    {
        var profiles = targetProfiles.Count <= fieldProfiles.Count
            ? targetProfiles
            : fieldProfiles;
        var otherProfiles = ReferenceEquals(profiles, targetProfiles)
            ? fieldProfiles
            : targetProfiles;

        foreach (var profile in profiles)
        {
            if (otherProfiles.Contains(profile))
                return true;
        }

        return false;
    }
}
