using System.Numerics;
using Content.Shared._Exodus.Visuals;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Visuals;

/// <summary>
/// Server-authoritative API for persistent and temporary visual links between entities.
/// </summary>
public sealed class EntityLinkVisualSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <summary>
    /// Creates or updates the single persistent link owned by <paramref name="source"/>.
    /// </summary>
    public bool TrySetLink(
        EntityUid source,
        EntityUid target,
        ProtoId<EntityLinkVisualPrototype> style,
        Vector2 sourceOffset = default,
        Vector2 targetOffset = default)
    {
        if (Deleted(source) || Deleted(target) || !_prototype.HasIndex(style))
            return false;

        var component = EnsureComp<EntityLinkVisualComponent>(source);
        if (component.Target == target &&
            component.Style == style &&
            component.SourceOffset == sourceOffset &&
            component.TargetOffset == targetOffset)
        {
            return true;
        }

        component.Target = target;
        component.Style = style;
        component.SourceOffset = sourceOffset;
        component.TargetOffset = targetOffset;
        Dirty(source, component);
        return true;
    }

    /// <summary>
    /// Updates only the target of an existing configured persistent link.
    /// </summary>
    public bool TrySetTarget(Entity<EntityLinkVisualComponent> source, EntityUid target)
    {
        if (Deleted(source.Owner) || Deleted(target) || !_prototype.HasIndex(source.Comp.Style))
            return false;

        if (source.Comp.Target == target)
            return true;

        source.Comp.Target = target;
        Dirty(source);
        return true;
    }

    /// <summary>
    /// Disables the persistent link without removing its reusable configuration component.
    /// </summary>
    public void ClearLink(Entity<EntityLinkVisualComponent> source)
    {
        if (source.Comp.Target == null)
            return;

        source.Comp.Target = null;
        Dirty(source);
    }

    /// <summary>
    /// Shows a temporary link to players near either endpoint.
    /// </summary>
    public bool TryShowTemporaryLink(
        EntityUid source,
        EntityUid target,
        ProtoId<EntityLinkVisualPrototype> style,
        TimeSpan duration,
        Vector2 sourceOffset = default,
        Vector2 targetOffset = default)
    {
        if (Deleted(source) ||
            Deleted(target) ||
            duration <= TimeSpan.Zero ||
            !_prototype.HasIndex(style))
        {
            return false;
        }

        var filter = Filter.Pvs(source, entityManager: EntityManager)
            .AddPlayersByPvs(target, entityManager: EntityManager);

        RaiseNetworkEvent(
            new EntityLinkVisualEvent(
                GetNetEntity(source),
                GetNetEntity(target),
                style,
                duration,
                sourceOffset,
                targetOffset),
            filter);
        return true;
    }
}
