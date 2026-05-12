using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Stacks;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Construction.Completions;

/// <summary>
/// Graph action that returns a fraction of machine parts from machine_parts container.
/// Amounts are read from the machine board's original requirements, not from what's physically in the container.
/// machine_board container is always fully returned.
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

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        var fraction = Math.Clamp(ReturnFraction, 0f, 1f);

        if (!entityManager.TryGetComponent(uid, out ContainerManagerComponent? containerManager))
            return;

        var containerSys = entityManager.EntitySysManager.GetEntitySystem<SharedContainerSystem>();
        var stackSys = entityManager.EntitySysManager.GetEntitySystem<StackSystem>();
        var xformSys = entityManager.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
        var protoManager = IoCManager.Resolve<IPrototypeManager>();
        var coords = xformSys.GetMapCoordinates(uid);
        var dropCoords = new EntityCoordinates(uid, 0, 0);

        // Find the board to read original requirements
        MachineBoardComponent? board = null;
        foreach (var container in containerManager.GetAllContainers())
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

            containerSys.EmptyContainer(container, true);
            break;
        }

        // Process machine_parts
        foreach (var container in containerManager.GetAllContainers())
        {
            if (container.ID != MachineFrameComponent.PartContainerName)
                continue;

            // Delete everything in the container — we spawn from board requirements
            foreach (var ent in container.ContainedEntities.ToArray())
            {
                containerSys.Remove(ent, container, reparent: false);
                entityManager.DeleteEntity(ent);
            }

            if (board == null)
                break;

            // Spawn stacks based on original StackRequirements
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

            // Spawn MachinePart entities based on original Requirements
            foreach (var (partType, amount) in board.Requirements)
            {
                var toReturn = (int) Math.Floor(amount * fraction);
                if (toReturn <= 0)
                    continue;

                var partProto = protoManager.Index(partType);
                for (var i = 0; i < toReturn; i++)
                    entityManager.SpawnEntity(partProto.StockPartPrototype, dropCoords);
            }

            break;
        }
    }
}
