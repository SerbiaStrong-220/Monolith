using Content.Shared._Exodus.NPC.Components;
using Content.Shared.Construction;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC;

public sealed partial class FactionNpcAiCoreControlSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionNpcAiCoreComponent, ComponentStartup>(OnCoreStartup);
        SubscribeLocalEvent<FactionNpcAiCoreComponent, ComponentShutdown>(OnCoreShutdown);
        SubscribeLocalEvent<FactionNpcAiCoreComponent, EntParentChangedMessage>(OnCoreParentChanged);
        SubscribeLocalEvent<FactionNpcAiCoreComponent, AnchorStateChangedEvent>(OnCoreAnchorChanged);
    }

    private void OnCoreStartup(Entity<FactionNpcAiCoreComponent> ent, ref ComponentStartup args)
    {
        RefreshCore(ent);
    }

    private void OnCoreShutdown(Entity<FactionNpcAiCoreComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.CurrentControlGrid is not { } oldGrid)
            return;

        ent.Comp.CurrentControlGrid = null;
        RefreshGrid(oldGrid, ent.Owner);
    }

    private void OnCoreParentChanged(Entity<FactionNpcAiCoreComponent> ent, ref EntParentChangedMessage args)
    {
        RefreshCore(ent);
    }

    private void OnCoreAnchorChanged(Entity<FactionNpcAiCoreComponent> ent, ref AnchorStateChangedEvent args)
    {
        RefreshCore(ent);
    }

    private void RefreshCore(Entity<FactionNpcAiCoreComponent> ent)
    {
        var oldGrid = ent.Comp.CurrentControlGrid;
        var newGrid = TryGetActiveCoreGrid(ent, out var activeGrid) ? activeGrid : (EntityUid?) null;

        ent.Comp.CurrentControlGrid = newGrid;

        if (oldGrid == newGrid)
        {
            if (newGrid is { } sameGrid)
                RefreshGrid(sameGrid);

            return;
        }

        if (oldGrid is { } previousGrid)
            RefreshGrid(previousGrid);

        if (newGrid is { } currentGrid)
            RefreshGrid(currentGrid);
    }

    private bool TryGetActiveCoreGrid(Entity<FactionNpcAiCoreComponent> core, out EntityUid grid)
    {
        grid = default;

        if (Deleted(core.Owner) ||
            !TryComp(core.Owner, out TransformComponent? xform) ||
            !IsActiveCoreOnGrid(xform, null))
        {
            return false;
        }

        grid = xform.GridUid!.Value;
        return true;
    }

    private bool IsActiveCoreOnGrid(TransformComponent xform, EntityUid? expectedGrid)
    {
        if (!xform.Anchored ||
            xform.GridUid is not { Valid: true } grid ||
            expectedGrid != null && grid != expectedGrid ||
            Deleted(grid) ||
            !HasComp<MapGridComponent>(grid))
        {
            return false;
        }

        return true;
    }

    private void RefreshGrid(EntityUid grid, EntityUid? ignoredCore = null)
    {
        if (Deleted(grid) || !HasComp<MapGridComponent>(grid))
            return;

        var foundCore = false;
        var contested = false;
        ProtoId<NpcFactionPrototype>? faction = null;

        var query = EntityQueryEnumerator<FactionNpcAiCoreComponent, TransformComponent>();
        while (query.MoveNext(out var coreUid, out var core, out var xform))
        {
            if (coreUid == ignoredCore ||
                !IsActiveCoreOnGrid(xform, grid))
            {
                continue;
            }

            if (!foundCore)
            {
                foundCore = true;
                faction = core.Faction;
                continue;
            }

            if (faction == core.Faction)
                continue;

            contested = true;
            break;
        }

        if (!foundCore)
        {
            RemCompDeferred<FactionAiControlledGridComponent>(grid);
            return;
        }

        var control = EnsureComp<FactionAiControlledGridComponent>(grid);
        var state = contested ? FactionAiControlState.Contested : FactionAiControlState.Controlled;
        var controlFaction = contested ? null : faction;

        if (control.State == state && control.Faction == controlFaction)
            return;

        control.State = state;
        control.Faction = controlFaction;
        Dirty(grid, control);
    }
}
