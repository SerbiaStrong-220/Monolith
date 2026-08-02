using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Visuals;

/// <summary>
/// Displays a temporary client-side visual link between two entities.
/// </summary>
[Serializable, NetSerializable]
public sealed class EntityLinkVisualEvent : EntityEventArgs
{
    public NetEntity Source { get; }
    public NetEntity Target { get; }
    public ProtoId<EntityLinkVisualPrototype> Style { get; }
    public TimeSpan Duration { get; }
    public Vector2 SourceOffset { get; }
    public Vector2 TargetOffset { get; }

    public EntityLinkVisualEvent(
        NetEntity source,
        NetEntity target,
        ProtoId<EntityLinkVisualPrototype> style,
        TimeSpan duration,
        Vector2 sourceOffset = default,
        Vector2 targetOffset = default)
    {
        Source = source;
        Target = target;
        Style = style;
        Duration = duration;
        SourceOffset = sourceOffset;
        TargetOffset = targetOffset;
    }
}
