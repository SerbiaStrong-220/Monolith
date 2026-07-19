using Content.Server.Actions;
using Content.Shared._Exodus.Movement;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Movement;

public sealed class TemporarySpeedBoostSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TemporarySpeedBoostComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TemporarySpeedBoostComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<TemporarySpeedBoostComponent, TemporarySpeedBoostActionEvent>(OnAction);
        SubscribeLocalEvent<TemporarySpeedBoostComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<TemporarySpeedBoostComponent, RefreshWeightlessModifiersEvent>(OnRefreshWeightlessSpeed);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TemporarySpeedBoostComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.EndsAt is not { } endsAt || endsAt > _timing.CurTime)
                continue;

            component.EndsAt = null;
            RefreshSpeed(uid);
        }
    }

    private void OnMapInit(Entity<TemporarySpeedBoostComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<TemporarySpeedBoostComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ActionEntity is { Valid: true } action)
            _actions.RemoveAction(action);
    }

    private void OnAction(Entity<TemporarySpeedBoostComponent> ent, ref TemporarySpeedBoostActionEvent args)
    {
        if (args.Handled || ent.Comp.EndsAt is { } endsAt && endsAt > _timing.CurTime)
            return;

        ent.Comp.EndsAt = _timing.CurTime + ent.Comp.Duration;
        RefreshSpeed(ent.Owner);
        args.Handled = true;
    }

    private void OnRefreshMovementSpeed(Entity<TemporarySpeedBoostComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!IsActive(ent.Comp))
            return;

        args.ModifySpeed(ent.Comp.WalkSpeedMultiplier, ent.Comp.SprintSpeedMultiplier);
    }

    private void OnRefreshWeightlessSpeed(Entity<TemporarySpeedBoostComponent> ent, ref RefreshWeightlessModifiersEvent args)
    {
        if (!IsActive(ent.Comp))
            return;

        args.ModifyAcceleration(ent.Comp.WeightlessAccelerationMultiplier, ent.Comp.WeightlessSpeedMultiplier);
    }

    private bool IsActive(TemporarySpeedBoostComponent component)
    {
        return component.EndsAt is { } endsAt && endsAt > _timing.CurTime;
    }

    private void RefreshSpeed(EntityUid uid)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
        _movement.RefreshWeightlessModifiers(uid);
    }
}
