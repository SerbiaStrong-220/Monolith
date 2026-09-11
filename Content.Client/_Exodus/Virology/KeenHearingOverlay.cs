using System.Numerics;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Virology;

public sealed class KeenHearingOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly TransformSystem _transform;
    private readonly ContainerSystem _containers;
    private readonly EntityLookupSystem _lookup;
    private readonly HashSet<Entity<MobStateComponent>> _targets = [];
    private TimeSpan _nextLookup;
    private EntityUid? _lastPlayer;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KeenHearingOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<TransformSystem>();
        _containers = _entities.System<ContainerSystem>();
        _lookup = _entities.System<EntityLookupSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player
            || !_entities.TryGetComponent<KeenHearingComponent>(player, out var hearing)
            || !hearing.Enabled
            || !_entities.TryGetComponent<MobStateComponent>(player, out var state)
            || state.CurrentState != MobState.Alive
            || !_entities.TryGetComponent<TransformComponent>(player, out var playerTransform))
            return;

        // Spatial lookup is throttled; rendering reuses the result and validates entity lifetime.
        if (_lastPlayer != player || _timing.CurTime >= _nextLookup)
        {
            _lastPlayer = player;
            _nextLookup = _timing.CurTime + TimeSpan.FromSeconds(0.2);
            _targets.Clear();
            _lookup.GetEntitiesInRange(playerTransform.Coordinates, hearing.VisionRadius, _targets);
        }

        var origin = _transform.GetWorldPosition(playerTransform);
        var pulse = (float)(_timing.CurTime.TotalSeconds % 1);
        var color = Color.Cyan.WithAlpha(0.8f * (1f - pulse));
        var handle = args.WorldHandle;
        foreach (var (uid, mob) in _targets)
        {
            if (uid == player || mob.Deleted || mob.CurrentState == MobState.Dead
                || !_entities.TryGetComponent<TransformComponent>(uid, out var transform)
                || transform.MapID != playerTransform.MapID)
                continue;

            var allowedStates = mob.AllowedStates;
            if (!allowedStates.Contains(MobState.Dead))
                continue;

            var position = _transform.GetWorldPosition(transform);
            var distance = Vector2.DistanceSquared(position, origin);
            if (distance > hearing.VisionRadius * hearing.VisionRadius
                || (distance > hearing.HighSensitiveVisionRadius * hearing.HighSensitiveVisionRadius
                    && _containers.IsEntityOrParentInContainer(uid)))
                continue;

            handle.DrawCircle(position, 0.1f + pulse * 0.3f, color, filled: false);
            handle.DrawCircle(position, 0.05f, color);
        }
    }
}
