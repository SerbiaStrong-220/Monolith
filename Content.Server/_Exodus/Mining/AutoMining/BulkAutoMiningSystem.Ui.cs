using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.BUIStates;
using Content.Shared.Damage;
using Content.Shared.Shuttles.BUIStates;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    public void UpdateUi(Entity<BulkAutoMiningConsoleComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, BulkAutoMiningUiKey.Key) || !TryComp<BulkAutoMiningJobComponent>(ent, out var job))
            return;

        ResolveEmitters(ent, job);
        if (!ent.Comp.Active)
            ent.Comp.SelectedGrids.RemoveAll(uid => TerminatingOrDeleted(uid));

        var targets = new List<BulkAutoMiningTargetState>(ent.Comp.SelectedGrids.Count);
        foreach (var target in ent.Comp.SelectedGrids)
        {
            if (!TerminatingOrDeleted(target))
                targets.Add(new BulkAutoMiningTargetState(GetNetEntity(target), Name(target)));
        }

        var lasers = new List<BulkAutoMiningLaserState>(job.Emitters.Count);
        var ready = false;
        foreach (var emitter in job.Emitters)
        {
            if (TerminatingOrDeleted(emitter) || !_emitterQuery.TryComp(emitter, out var emitterComp))
                continue;

            var status = GetEmitterStatus(ent, emitter);
            ready |= status == BulkAutoMiningLaserStatus.Ready;
            if (ent.Comp.Active && status == BulkAutoMiningLaserStatus.Ready && job.Statuses.TryGetValue(emitter, out var miningStatus))
                status = miningStatus;

            var maxHp = _destructible.DestroyedAt(emitter).Float();
            var hp = TryComp<DamageableComponent>(emitter, out var damageable) ? Math.Max(0, maxHp - damageable.TotalDamage.Float()) : 0;
            var stored = _materials.GetTotalMaterialAmount(emitter, localOnly: true);
            var capacity = _storageQuery.TryComp(emitter, out var storage) ? storage.StorageLimit ?? 0 : 0;
            var consoleName = emitterComp.ConsoleName is { } name ? Loc.GetString(name) : null;
            var warmup = GetWarmup((emitter, emitterComp));
            var yieldBonus = GetWarmupYieldBonus((emitter, emitterComp), warmup);
            lasers.Add(new BulkAutoMiningLaserState(GetNetEntity(emitter), Name(emitter), hp, maxHp, status, stored, capacity,
                consoleName, (float)warmup, (float)yieldBonus));
        }

        // Mining does not display docking or grappling controls. Avoid collecting them across every ship.
        var nav = _shuttleConsole.GetNavState(ent.Owner,
            new Dictionary<NetEntity, List<DockingPortState>>(), new List<GrapplingLinkState>());
        nav.MaxRange = ent.Comp.MaxRange;
        _ui.SetUiState(ent.Owner, BulkAutoMiningUiKey.Key, new BulkAutoMiningBoundUserInterfaceState(
            nav, targets, lasers.Count, ent.Comp.ProcessedTiles, ent.Comp.TotalTiles, ent.Comp.Active,
            lasers, IsPoweredAndAnchored(ent) && ready));
    }
}
