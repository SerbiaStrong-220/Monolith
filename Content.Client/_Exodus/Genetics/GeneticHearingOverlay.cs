using System.Numerics;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Genetics;

/// <summary>A brief pulse using only nearby entities already available to this client.</summary>
public sealed class GeneticHearingOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly TransformSystem _transform;
    private readonly ContainerSystem _containers;
    private readonly EntityLookupSystem _lookup;
    private readonly MobStateSystem _mobStates;
    private readonly HashSet<Entity<MobStateComponent>> _targets = new();
    private TimeSpan _nextLookup;
    private EntityUid? _lastPlayer;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public GeneticHearingOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<TransformSystem>();
        _containers = _entities.System<ContainerSystem>();
        _lookup = _entities.System<EntityLookupSystem>();
        _mobStates = _entities.System<MobStateSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entities.TryGetComponent<GeneticEffectsComponent>(player, out var effects) ||
            effects.Reverting || !effects.HearingEnabled ||
            (effects.Modifiers.Abilities & GeneticAbility.Hearing) == 0 ||
            !_entities.TryGetComponent<MobStateComponent>(player, out var mob) || mob.CurrentState != MobState.Alive ||
            !_entities.TryGetComponent<TransformComponent>(player, out var playerTransform))
        {
            _targets.Clear();
            _lastPlayer = null;
            return;
        }

        if (_lastPlayer != player || _timing.CurTime >= _nextLookup)
        {
            _lastPlayer = player;
            _nextLookup = _timing.CurTime + TimeSpan.FromSeconds(0.2);
            _targets.Clear();
            _lookup.GetEntitiesInRange(playerTransform.Coordinates, effects.HearingRange, _targets);
        }

        var origin = _transform.GetWorldPosition(playerTransform);
        var pulse = (float) (_timing.CurTime.TotalSeconds % 1);
        var color = Color.Cyan.WithAlpha(0.8f * (1f - pulse));
        foreach (var (uid, target) in _targets)
        {
            if (uid == player || target.Deleted || target.CurrentState == MobState.Dead ||
                !_mobStates.HasState(uid, MobState.Dead, target) || _containers.IsEntityOrParentInContainer(uid) ||
                !_entities.TryGetComponent<TransformComponent>(uid, out var transform) ||
                transform.MapID != playerTransform.MapID)
                continue;

            var position = _transform.GetWorldPosition(transform);
            if (Vector2.DistanceSquared(position, origin) > effects.HearingRange * effects.HearingRange)
                continue;

            args.WorldHandle.DrawCircle(position, 0.1f + pulse * 0.3f, color, filled: false);
            args.WorldHandle.DrawCircle(position, 0.05f, color);
        }
    }
}
