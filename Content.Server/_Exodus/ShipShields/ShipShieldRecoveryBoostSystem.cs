using Content.Server._Crescent.ShipShields;
using Content.Server._Crescent.ShipShields.Components;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.ShipShields;

public sealed partial class ShipShieldRecoveryBoostSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<ShipShieldEmitterComponent> _emitterQuery;
    private EntityQuery<ShipShieldedComponent> _shieldedQuery;
    private EntityQuery<ShipShieldDisabledGridComponent> _disabledGridQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _emitterQuery = GetEntityQuery<ShipShieldEmitterComponent>();
        _shieldedQuery = GetEntityQuery<ShipShieldedComponent>();
        _disabledGridQuery = GetEntityQuery<ShipShieldDisabledGridComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<ShipShieldRecoveryBoostComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipShieldRecoveryBoostComponent, ShipShieldHitEvent>(OnHit);
        SubscribeLocalEvent<ShipShieldRecoveryBoostComponent, ShipShieldRegenerationEvent>(OnRegeneration);
    }

    private void OnMapInit(Entity<ShipShieldRecoveryBoostComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.BoostReadyAt == TimeSpan.Zero)
            ResetDelay(ent);
    }

    private void OnHit(Entity<ShipShieldRecoveryBoostComponent> ent, ref ShipShieldHitEvent args)
    {
        ResetDelay(ent);
    }

    private void ResetDelay(Entity<ShipShieldRecoveryBoostComponent> ent)
    {
        ent.Comp.BoostReadyAt = _timing.CurTime + ent.Comp.RecoveryDelay;
    }

    private void OnRegeneration(Entity<ShipShieldRecoveryBoostComponent> ent, ref ShipShieldRegenerationEvent args)
    {
        if (!args.Active ||
            _timing.CurTime < ent.Comp.BoostReadyAt ||
            EntityManager.IsQueuedForDeletion(ent.Owner) ||
            !_emitterQuery.TryGetComponent(ent, out var emitter) ||
            ShipShieldsSystem.IsDamageOverloaded(emitter) ||
            emitter.Shield is not { } shield ||
            emitter.Shielded is not { } grid ||
            TerminatingOrDeleted(shield) ||
            EntityManager.IsQueuedForDeletion(shield) ||
            !_transformQuery.TryGetComponent(ent, out var xform) ||
            xform.GridUid != grid ||
            _disabledGridQuery.HasComp(grid) ||
            !_shieldedQuery.TryGetComponent(grid, out var shielded) ||
            shielded.Source != ent.Owner ||
            shielded.Shield != shield)
        {
            return;
        }

        args.Amount *= Math.Max(1f, ent.Comp.Multiplier);
    }
}
