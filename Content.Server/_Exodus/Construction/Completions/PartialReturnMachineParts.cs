using System.Linq;
using Content.Server.Construction.Components;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._Exodus.Construction.Completions;

/// <summary>
/// Graph action that returns a fraction of machine parts from machine_parts container.
/// Use instead of EmptyAllContainers when partial material return on deconstruction is desired.
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

        foreach (var container in containerManager.GetAllContainers())
        {
            if (container.ID == MachineFrameComponent.BoardContainerName)
            {
                containerSys.EmptyContainer(container, true);
                continue;
            }

            if (container.ID != MachineFrameComponent.PartContainerName)
                continue;

            var entities = container.ContainedEntities.ToArray();
            var toReturn = (int) Math.Floor(entities.Length * fraction);

            for (var i = 0; i < entities.Length; i++)
            {
                var ent = entities[i];
                if (i < toReturn)
                    containerSys.Remove(ent, container, reparent: true);
                else
                    entityManager.DeleteEntity(ent);
            }
        }
    }
}
