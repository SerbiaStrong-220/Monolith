using Content.Server.Construction;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Exodus.Stack;
using Content.Shared.Stacks;

namespace Content.Server._Exodus.Stack;

public sealed partial class StackItemSystem : EntitySystem
{
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StackItemComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<StackItemComponent, ConstructionChangeEntityEvent>(OnConstructionChange);
    }

    private void OnTrigger(Entity<StackItemComponent> ent, ref TriggerEvent args)
    {
        if (!ent.Comp.ConsumeOnTrigger || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
            return;

        if (TryComp<StackComponent>(ent, out var stack))
            args.Handled |= _stacks.Use(ent, 1, stack);
        else
        {
            QueueDel(ent);
            args.Handled = true;
        }
    }

    /// <summary>
    /// Entity replacement crafts one item; leave the unused units beside the result.
    /// </summary>
    private void OnConstructionChange(Entity<StackItemComponent> ent, ref ConstructionChangeEntityEvent args)
    {
        if (args.Old != ent.Owner || !TryComp<StackComponent>(ent, out var stack) || stack.Count <= 1)
            return;

        _stacks.Split(ent, stack.Count - 1, _transform.GetMoverCoordinates(ent), stack);
    }

    /// <summary>
    /// Supplies one item to an entity-based construction step. Ordinary entities are returned unchanged.
    /// </summary>
    public bool TryTakeOne(EntityUid item, out EntityUid single)
    {
        single = item;
        if (TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item))
            return false;

        if (!HasComp<StackItemComponent>(item) || !TryComp<StackComponent>(item, out var stack))
            return true;

        if (stack.Count <= 0)
            return false;

        if (stack.Count == 1)
            return true;

        if (_stacks.Split(item, 1, _transform.GetMoverCoordinates(item), stack) is not { } split)
            return false;

        single = split;
        return true;
    }
}
