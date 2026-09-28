using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Shuttles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Shuttles;

/// <summary>Releases forced anchors through the normal shuttle physics lifecycle.</summary>
public sealed partial class GridAnchorReleaseSystem : EntitySystem
{
    [Dependency] private ShuttleSystem _shuttles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridAnchorReleasedComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    public bool TryRelease(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid) || !HasComp<PhysicsComponent>(grid)
            || !HasComp<FixturesComponent>(grid)
            || TryComp<ForceAnchorComponent>(grid, out var forceAnchor) && forceAnchor.Bedrock
            || TryComp<FTLComponent>(grid, out var ftl) && ftl.State is not (FTLState.Available or FTLState.Cooldown))
            return false;

        EnsureComp<GridAnchorReleasedComponent>(grid);
        RemCompDeferred<PreventGridAnchorChangesComponent>(grid);
        var shuttle = EnsureComp<ShuttleComponent>(grid);
        return _shuttles.TrySetEnabled((grid, shuttle), true, force: true);
    }

    private void OnMapInit(Entity<GridAnchorReleasedComponent> ent, ref MapInitEvent args) => TryRelease(ent);

    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!HasComp<GridAnchorReleasedComponent>(args.Grid))
            return;

        foreach (var grid in args.NewGrids)
            TryRelease(grid);
    }
}
