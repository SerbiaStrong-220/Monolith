using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Stacks;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Construction.Completions;

/// <summary>
/// Graph action that returns a fraction of machine parts from the machine_parts container.
/// Stacks use original board requirements for correct amounts (container clips to maxCount).
/// Non-stack entities (MachinePart) are returned directly from the container, preserving upgrades.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class PartialReturnMachineParts : IGraphAction
{
    /// <summary>
    /// Fraction of parts to return. 0.35 = 35%, 1.0 = 100%.
    /// Clamped to [0, 1].
    /// </summary>
    [DataField(required: true)]
    public float ReturnFraction = 1.0f;

    /// <summary>
    /// Whether the machine board is returned intact. When false, the board is destroyed
    /// along with the rest of the machine.
    /// </summary>
    [DataField]
    public bool ReturnBoard = true;

    /// <summary>
    /// Whether non-stack parts (manipulators, capacitors, etc.) are returned as their real
    /// entities to preserve upgrade ratings. When false, they are destroyed instead.
    /// </summary>
    [DataField]
    public bool PreserveNonStackParts = true;

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        var fraction = Math.Clamp(ReturnFraction, 0f, 1f);

        if (!entityManager.TryGetComponent(uid, out ContainerManagerComponent? containerManager))
            return;

        var containerSys = entityManager.System<SharedContainerSystem>();
        var stackSys = entityManager.System<StackSystem>();
        var xformSys = entityManager.System<SharedTransformSystem>();
        var protoManager = IoCManager.Resolve<IPrototypeManager>();
        var coords = xformSys.GetMapCoordinates(uid);
        var dropCoords = new EntityCoordinates(uid, 0, 0);

        // Read board requirements before emptying
        MachineBoardComponent? board = null;
        foreach (var (key, container) in containerManager.Containers)
        {
            if (container.ID != MachineFrameComponent.BoardContainerName)
                continue;

            foreach (var boardEnt in container.ContainedEntities)
            {
                if (entityManager.TryGetComponent(boardEnt, out MachineBoardComponent? b))
                {
                    board = b;
                    break;
                }
            }

            // EmptyContainer with reparent drops the board back into the world (returned).
            // Otherwise the board is destroyed along with the machine.
            if (ReturnBoard)
                containerSys.EmptyContainer(container, true);
            else
                containerSys.CleanContainer(container);
            break;
        }

        foreach (var (key, container) in containerManager.Containers)
        {
            if (container.ID != MachineFrameComponent.PartContainerName)
                continue;

            var entities = container.ContainedEntities.ToArray();

            // Separate stacks from non-stacks
            var stackEntities = new List<EntityUid>();
            var nonStackGroups = new Dictionary<string, List<EntityUid>>();

            foreach (var ent in entities)
            {
                // MachinePartComponent entities (Manipulator, MatterBin, etc.) have StackComponent count=1
                // but must be returned directly to preserve upgrade ratings
                if (!entityManager.HasComponent<MachinePartComponent>(ent) &&
                    entityManager.HasComponent<StackComponent>(ent))
                {
                    stackEntities.Add(ent);
                }
                else
                {
                    var proto = entityManager.GetComponentOrNull<MetaDataComponent>(ent)?.EntityPrototype?.ID
                                ?? ent.ToString();
                    if (!nonStackGroups.TryGetValue(proto, out var group))
                        nonStackGroups[proto] = group = new List<EntityUid>();
                    group.Add(ent);
                }
            }

            // Delete all stack entities - we spawn from board requirements for correct amounts
            foreach (var ent in stackEntities)
            {
                containerSys.Remove(ent, container, reparent: false);
                entityManager.DeleteEntity(ent);
            }

            // Spawn stacks from board StackRequirements
            if (board != null)
            {
                foreach (var (stackType, amount) in board.StackRequirements)
                {
                    var toReturn = (int) Math.Floor(amount * fraction);
                    if (toReturn <= 0)
                        continue;

                    var stackProto = protoManager.Index(stackType);
                    var spawned = stackSys.SpawnMultiple(stackProto.Spawn, toReturn, dropCoords);
                    foreach (var s in spawned)
                        xformSys.SetMapCoordinates(s, coords);
                }
            }

            // Return non-stack entities directly (preserves upgrade ratings), or destroy them all
            // when preservation is disabled.
            foreach (var (_, group) in nonStackGroups)
            {
                var toReturn = PreserveNonStackParts ? (int) Math.Floor(group.Count * fraction) : 0;
                for (var i = 0; i < group.Count; i++)
                {
                    var ent = group[i];
                    if (i < toReturn)
                        containerSys.Remove(ent, container, reparent: true);
                    else
                        entityManager.DeleteEntity(ent);
                }
            }

            break;
        }
    }
}
