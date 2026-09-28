using Content.Server.Destructible;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private RotOrganDecaySystem _organDecay = default!;
    private readonly HashSet<EntityUid> _placement = [];

    private void InitializeConstruction()
    {
        SubscribeLocalEvent<RotConstructionComponent, RotConstructionEvent>(OnConstructionDone);
        SubscribeLocalEvent<RotConstructionComponent, ComponentShutdown>(OnConstructionShutdown);
    }

    public void CopyColonyStrain(EntityUid core, EntityUid offspring)
    {
        if (TryComp<Content.Shared._Exodus.Virology.Lifecycle.VirusReservoirComponent>(core, out var strain)
            && strain.Strain is { } source && TryComp<Content.Shared._Exodus.Virology.Lifecycle.VirusReservoirComponent>(offspring, out var reservoir))
        {
            reservoir.Strain = Lifecycle.VirusLifecycleSystem.FreshInfection(source);
            reservoir.Identity = null;
        }
    }

    private void RestoreProjects(Entity<RotIntelligentComponent, RotColonyStateComponent> ent)
    {
        var state = ent.Comp2;
        state.ProjectsRestored = true;
        state.Scratch.Clear();
        state.Scratch.AddRange(state.Projects);
        foreach (var uid in state.Scratch)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<RotConstructionComponent>(uid, out var job) || job.Core != ent.Owner)
            {
                state.Projects.Remove(uid);
                continue;
            }
            var conflict = false;
            foreach (var cell in RotGeometry.Cells(job.Tile, job.Size, job.Rotation))
            {
                if (state.Reservations.TryGetValue(cell, out var owner) && owner != uid)
                    conflict = true;
                else
                    state.Reservations[cell] = uid;
            }
            // Native DoAfters are not guaranteed to survive map serialization. Settle incomplete work once.
            if (conflict || !job.WaitingForNetwork && !job.Ready
                && _doAfter.GetStatus(job.DoAfter) is not (DoAfterStatus.Running or DoAfterStatus.Finished))
                CancelProject(uid, refund: true);
        }
    }

    public bool TryQueueBuilding(Entity<RotIntelligentComponent> ent, EntityCoordinates coordinates,
        ProtoId<RotBuildingPrototype> recipeId, int rotation)
    {
        if (!ent.Comp.Buildings.Contains(recipeId) || !_prototypes.TryIndex(recipeId, out var recipe)
            || !TryBuildContext(ent, coordinates, out var state, out var grid, out var tile))
            return false;
        if (!ValidateArea(ent, state, grid, tile, recipe, rotation, null, out var wall, out var reason,
                checkSupport: ent.Comp.NetworkReady))
        {
            Feedback(ent, reason);
            return false;
        }
        if (ent.Comp.Biomass < recipe.Cost)
        {
            Feedback(ent, "rot-intelligent-insufficient-biomass");
            return false;
        }
        var duration = recipe.Duration;
        if (wall is { } target)
            duration += TimeSpan.FromSeconds(Math.Max(0, _destructible.DestroyedAt(target).Float()
                - Comp<DamageableComponent>(target).TotalDamage.Float()) * recipe.ConversionSecondsPerDamage);
        var job = CreateProject(ent, state, grid, tile, recipe.Size, rotation, recipe.Cost, duration);
        var project = Comp<RotConstructionComponent>(job);
        project.Building = recipeId;
        project.Target = wall;
        if (wall is { } original)
            project.OriginalDamage = Comp<DamageableComponent>(original).TotalDamage.Float();
        if (!ent.Comp.NetworkReady)
        {
            project.WaitingForNetwork = true;
            return true;
        }
        return StartProject((job, project));
    }

    private bool TryQueueMaintenance(Entity<RotIntelligentComponent> ent, EntityUid target, RotProjectKind kind)
    {
        if (TerminatingOrDeleted(target) || target == ent.Owner || !_members.TryComp(target, out var member)
            || member.Core != ent.Owner || !member.NeedsSupport || !member.Connected
            || !TryBuildContext(ent, Transform(target).Coordinates, out var state, out var grid, out var tile))
            return false;
        foreach (var uid in state.Projects)
        {
            if (TryComp<RotConstructionComponent>(uid, out var active) && active.Target == target)
            {
                Feedback(ent, "rot-intelligent-occupied");
                return false;
            }
        }
        var amount = kind == RotProjectKind.Repair && TryComp<DamageableComponent>(target, out var damage)
            ? MathF.Min(ent.Comp.RepairAmount, damage.TotalDamage.Float()) : 0;
        if (kind == RotProjectKind.Repair && (amount <= 0 || state.Alerts.ContainsKey(target)))
        {
            Feedback(ent, "rot-intelligent-repair-unavailable");
            return false;
        }
        var cost = amount * ent.Comp.RepairCostPerDamage;
        if (cost > ent.Comp.Biomass)
        {
            Feedback(ent, "rot-intelligent-insufficient-biomass");
            return false;
        }
        var rotation = RotGeometry.QuarterTurns(Transform(target).LocalRotation);
        foreach (var cell in RotGeometry.Cells(tile, member.Size, rotation))
        {
            if (state.Reservations.ContainsKey(cell))
                return false;
        }
        var job = CreateProject(ent, state, grid, tile, member.Size, rotation, cost, ent.Comp.MaintenanceDuration);
        var project = Comp<RotConstructionComponent>(job);
        project.Kind = kind;
        project.Target = target;
        project.OriginalDamage = amount;
        return StartProject((job, project));
    }

    private EntityUid CreateProject(Entity<RotIntelligentComponent> ent, RotColonyStateComponent state,
        Entity<MapGridComponent> grid, Vector2i tile, Vector2i size, int rotation, float cost, TimeSpan duration)
    {
        var uid = Spawn(ent.Comp.ConstructionMarker, _maps.GridTileToLocal(grid, grid.Comp, tile));
        _transform.SetLocalRotation(uid, Angle.FromDegrees((rotation & 3) * 90));
        var project = EnsureComp<RotConstructionComponent>(uid);
        project.Core = ent;
        project.Tile = tile;
        project.Size = size;
        project.Rotation = rotation & 3;
        project.Reserved = cost;
        project.Duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(0.1);
        project.Started = _timing.CurTime;
        foreach (var cell in RotGeometry.Cells(tile, size, rotation))
            state.Reservations.Add(cell, uid);
        state.Projects.Add(uid);
        ent.Comp.Biomass -= cost;
        Dirty(ent);
        return uid;
    }

    private bool StartProject(Entity<RotConstructionComponent> ent)
    {
        var (uid, project) = ent;
        project.WaitingForNetwork = false;
        project.Started = _timing.CurTime;
        // The marker owns the DoAfter: its progress is shown over the growing organ while the eye stays free.
        var args = new DoAfterArgs(EntityManager, uid, project.Duration, new RotConstructionEvent(), uid, target: project.Target)
        {
            NeedHand = false,
            BreakOnMove = false,
            BreakOnDamage = false,
            MultiplyDelay = false,
            CancelDuplicate = false,
        };
        if (_doAfter.TryStartDoAfter(args, out project.DoAfter))
            return true;
        CancelProject(uid, refund: true);
        return false;
    }

    private void OnConstructionDone(Entity<RotConstructionComponent> ent, ref RotConstructionEvent args)
    {
        if (args.Handled || ent.Comp.Settled || ent.Comp.DoAfter != args.DoAfter.Id)
            return;
        args.Handled = true;
        ent.Comp.DoAfter = null;
        if (args.Cancelled)
        {
            CancelProject(ent, refund: true);
            return;
        }
        ent.Comp.Ready = true;
        // Another organ may have completed during this tick. Await the new connectivity result, not a stale cache.
        if (TryComp<RotIntelligentComponent>(ent.Comp.Core, out var core) && core.ConstructionAvailable)
            CompleteProject(ent);
    }

    private void OnConstructionShutdown(Entity<RotConstructionComponent> ent, ref ComponentShutdown args) => SettleProject(ent, refund: true);

    private void ValidateProjects(Entity<RotIntelligentComponent, RotColonyStateComponent> ent)
    {
        ent.Comp2.Scratch.Clear();
        ent.Comp2.Scratch.AddRange(ent.Comp2.Projects);
        foreach (var uid in ent.Comp2.Scratch)
        {
            if (!TryComp<RotConstructionComponent>(uid, out var project) || project.Settled)
                continue;
            if (Transform(uid).GridUid != ent.Comp2.Grid || project.Target is { } target && TerminatingOrDeleted(target))
            {
                CancelProject(uid, refund: true);
                continue;
            }
            if (!ent.Comp1.ConstructionAvailable)
                continue;
            if (project.WaitingForNetwork)
            {
                if (!ent.Comp1.NetworkReady)
                    continue;
                if (project.Building is not { } recipeId || !_prototypes.TryIndex(recipeId, out var recipe)
                    || ent.Comp2.Grid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid)
                    || !ValidateArea((ent.Owner, ent.Comp1), ent.Comp2, (grid, mapGrid), project.Tile, recipe,
                        project.Rotation, uid, out var wall, out _) || wall != project.Target)
                {
                    Feedback(ent, "rot-intelligent-project-invalid");
                    CancelProject(uid, refund: true);
                    continue;
                }
                project.Duration = recipe.Duration;
                if (wall is { } original && TryComp<DamageableComponent>(original, out var damage))
                {
                    project.OriginalDamage = damage.TotalDamage.Float();
                    project.Duration += TimeSpan.FromSeconds(Math.Max(0, _destructible.DestroyedAt(original).Float()
                        - project.OriginalDamage) * recipe.ConversionSecondsPerDamage);
                }
                StartProject((uid, project));
                continue;
            }
            var supported = true;
            foreach (var cell in RotGeometry.Cells(project.Tile, project.Size, project.Rotation))
            {
                if (ent.Comp2.Connected.Contains(cell) || HasConnectedNeighbor(ent.Comp2, cell))
                    continue;
                supported = false;
                break;
            }
            if (!supported)
                CancelProject(uid, refund: true);
            else if (project.Ready)
                CompleteProject((uid, project));
        }
    }

    private void CompleteProject(Entity<RotConstructionComponent> ent)
    {
        var project = ent.Comp;
        if (project.Settled || !IsLivingCore(project.Core) || !TryComp<RotIntelligentComponent>(project.Core, out var core)
            || !TryComp<RotColonyStateComponent>(project.Core, out var state) || state.Grid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            CancelProject(ent, refund: true);
            return;
        }
        if (project.Kind == RotProjectKind.Build && project.Building is { } id)
        {
            var recipe = _prototypes.Index(id);
            if (!ValidateArea((project.Core, core), state, (grid, mapGrid), project.Tile, recipe, project.Rotation,
                ent, out var wall, out _) || wall != project.Target
                || wall is { } original && Comp<DamageableComponent>(original).TotalDamage.Float() != project.OriginalDamage)
            {
                CancelProject(ent, refund: true);
                return;
            }
            // Spawn the airtight replacement first. Queueing the original deletion avoids a transient open tile and loot.
            var built = Spawn(recipe.Entity, _maps.GridTileToLocal(grid, mapGrid, project.Tile));
            _transform.SetLocalRotation(built, Angle.FromDegrees(project.Rotation * 90));
            var member = EnsureComp<RotColonyMemberComponent>(built);
            member.Refund = project.Reserved * core.SalvageFraction;
            Join(built, project.Core);
            CopyColonyStrain(project.Core, built);
            if (wall is { } replaced)
                QueueDel(replaced);
        }
        else if (project.Target is { } target && !TerminatingOrDeleted(target) && _members.TryComp(target, out var member)
            && member.Core == project.Core && member.Connected)
        {
            if (project.Kind == RotProjectKind.Dissolve)
            {
                core.Biomass = MathF.Min(core.Capacity, core.Biomass + member.Refund);
                _organDecay.Play(target);
                QueueDel(target);
            }
            else if (TryComp<DamageableComponent>(target, out var damage))
            {
                var healing = new DamageSpecifier();
                var remaining = FixedPoint2.New(project.OriginalDamage);
                foreach (var (type, value) in damage.Damage.DamageDict)
                {
                    var amount = FixedPoint2.Min(remaining, value);
                    if (amount <= FixedPoint2.Zero)
                        continue;
                    healing.DamageDict[type] = -amount;
                    remaining -= amount;
                }
                _damage.TryChangeDamage(target, healing, true);
                core.Biomass = MathF.Min(core.Capacity, core.Biomass + remaining.Float() * core.RepairCostPerDamage);
            }
            Dirty(project.Core, core);
        }
        else
        {
            CancelProject(ent, refund: true);
            return;
        }
        SettleProject(ent, refund: false);
        QueueDel(ent);
    }

    private void CancelProject(EntityUid uid, bool refund)
    {
        if (!TryComp<RotConstructionComponent>(uid, out var project) || project.Settled)
            return;
        SettleProject((uid, project), refund);
        if (project.DoAfter is { } id)
            _doAfter.Cancel(id);
        QueueDel(uid);
    }

    private void SettleProject(Entity<RotConstructionComponent> ent, bool refund)
    {
        if (ent.Comp.Settled)
            return;
        ent.Comp.Settled = true;
        if (!TryComp<RotColonyStateComponent>(ent.Comp.Core, out var state))
            return;
        state.Projects.Remove(ent);
        foreach (var cell in RotGeometry.Cells(ent.Comp.Tile, ent.Comp.Size, ent.Comp.Rotation))
        {
            if (state.Reservations.TryGetValue(cell, out var owner) && owner == ent.Owner)
                state.Reservations.Remove(cell);
        }
        if (refund && TryComp<RotIntelligentComponent>(ent.Comp.Core, out var core) && core.Alive)
        {
            var progress = ent.Comp.WaitingForNetwork ? 0f
                : Math.Clamp((float)((_timing.CurTime - ent.Comp.Started) / ent.Comp.Duration), 0f, 1f);
            core.Biomass = MathF.Min(core.Capacity, core.Biomass + ent.Comp.Reserved * (1 - progress));
            Dirty(ent.Comp.Core, core);
        }
    }
}
