using System.Numerics;
using Content.Server._Exodus.Destructible;
using Content.Server.Stack;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Stack;

public sealed partial class StackSpawnSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Validates before spawning anything, including the output stack's capacity.
    /// </summary>
    public bool IsValid(SpawnStackBehavior behavior)
    {
        if (behavior.AmountPerUnit <= 0 || !float.IsFinite(behavior.Offset) || behavior.Offset < 0 ||
            behavior.Sizes.Count == 0 || behavior.Sizes[^1] != 1 ||
            !_prototypes.TryIndex(behavior.Stack, out var stack) ||
            !_prototypes.TryIndex(stack.Spawn, out var entity) ||
            !entity.TryGetComponent<StackComponent>(out var component, EntityManager.ComponentFactory) ||
            component.StackTypeId != stack.ID)
            return false;

        var previous = (long) _stacks.GetMaxCount(component) + 1;
        foreach (var size in behavior.Sizes)
        {
            if (size <= 0 || size >= previous)
                return false;

            previous = size;
        }

        return true;
    }

    public void SpawnDrops(EntityUid source, SpawnStackBehavior behavior)
    {
        if (TerminatingOrDeleted(source))
            return;

        if (!IsValid(behavior))
        {
            Log.Error($"Invalid stack drop configuration on {ToPrettyString(source)} for {behavior.Stack}.");
            return;
        }

        var amount = (long) _stacks.GetCount(source) * behavior.AmountPerUnit;
        if (amount <= 0)
            return;

        // Use grid-relative coordinates so moving grids keep their drops.
        var coordinates = _transform.GetMoverCoordinates(source);
        var stack = _prototypes.Index(behavior.Stack);
        foreach (var size in behavior.Sizes)
        {
            var count = amount / size;
            amount %= size;
            for (long i = 0; i < count; i++)
            {
                var offset = new Vector2(_random.NextFloat(-behavior.Offset, behavior.Offset),
                    _random.NextFloat(-behavior.Offset, behavior.Offset));
                _stacks.Spawn(size, stack, coordinates.Offset(offset));
            }
        }
    }
}
