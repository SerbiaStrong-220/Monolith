using System.Numerics;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed class RotColonySiteSystem : ComponentTreeSystem<RotColonySiteTreeComponent, RotColonySiteComponent>
{
    protected override bool DoFrameUpdate => false;
    protected override bool DoTickUpdate => true;
    protected override bool Recursive => true;

    protected override Box2 ExtractAabb(in ComponentTreeEntry<RotColonySiteComponent> entry, Vector2 pos, Angle rot)
        => Box2.CenteredAround(pos, new Vector2(0.1f));

    /// <summary>Preserves unlimited home search on the current grid, and across the map only when stranded in space.</summary>
    public void GetSites(EntityUid searcher, List<Entity<RotColonySiteComponent, TransformComponent>> results)
    {
        results.Clear();
        UpdateTreePositions();
        var transform = Transform(searcher);
        if (transform.GridUid is { } grid)
        {
            if (TryComp<RotColonySiteTreeComponent>(grid, out var tree))
                AppendSites(tree, results);
            return;
        }
        if (transform.MapUid == null)
            return;

        var trees = EntityQueryEnumerator<RotColonySiteTreeComponent, TransformComponent>();
        while (trees.MoveNext(out _, out var tree, out var xform))
        {
            if (xform.MapUid == transform.MapUid)
                AppendSites(tree, results);
        }
    }

    private void AppendSites(RotColonySiteTreeComponent tree, List<Entity<RotColonySiteComponent, TransformComponent>> results)
    {
        foreach (var entry in tree.Tree)
        {
            if (!TerminatingOrDeleted(entry.Uid) && !Paused(entry.Uid))
                results.Add(entry);
        }
    }
}
