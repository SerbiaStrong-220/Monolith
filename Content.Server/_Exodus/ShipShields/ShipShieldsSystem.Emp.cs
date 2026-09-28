using Content.Shared.Emp;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private void InitializeEmpProtection()
    {
        SubscribeLocalEvent<TransformComponent, EmpAttemptEvent>(OnShieldEmpAttempt);
    }

    private void OnShieldEmpAttempt(Entity<TransformComponent> ent, ref EmpAttemptEvent args)
    {
        // Resolve the active field through the target's grid, including items inside containers.
        // No per-tick scans or immunity components on individual ship equipment are needed.
        if (args.Cancelled ||
            ent.Comp.GridUid is not { } grid ||
            !_shieldedQuery.TryGetComponent(grid, out var shielded) ||
            shielded.Source is not { } source ||
            !_shieldEmitterQuery.TryGetComponent(source, out var emitter) ||
            !emitter.EmpProtection ||
            emitter.Shield != shielded.Shield ||
            emitter.Shielded != grid ||
            emitter.Recharging ||
            emitter.OverloadAccumulator > 0f ||
            IsDamageOverloaded(emitter) ||
            TerminatingOrDeleted(source) ||
            EntityManager.IsQueuedForDeletion(source) ||
            TerminatingOrDeleted(shielded.Shield) ||
            EntityManager.IsQueuedForDeletion(shielded.Shield) ||
            !_transformQuery.TryGetComponent(source, out var sourceXform) ||
            sourceXform.GridUid != grid ||
            !_apcPowerReceiverQuery.TryGetComponent(source, out var power) ||
            !power.Powered || power.PowerDisabled)
        {
            return;
        }

        args.Cancelled = true;
    }
}
