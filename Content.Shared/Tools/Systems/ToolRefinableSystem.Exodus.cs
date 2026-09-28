// Exodus: refining a stack of individual items consumes one unit.
using Content.Shared._Exodus.Stack;
using Content.Shared.Stacks;

namespace Content.Shared.Tools.Systems;

public sealed partial class ToolRefinablSystem
{
    [Dependency] private SharedStackSystem _stacks = default!;

    private bool TryConsumeRefinedItem(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
            return false;

        if (HasComp<StackItemComponent>(uid) && TryComp<StackComponent>(uid, out var stack))
            return _stacks.Use(uid, 1, stack);

        QueueDel(uid);
        return true;
    }
}
