using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Construction;

/// <summary>
/// Displays a temporary client-side beam after a successful remote part exchange.
/// </summary>
[Serializable, NetSerializable]
public sealed class PartExchangeVisualEvent : EntityEventArgs
{
    public NetEntity User { get; }
    public NetEntity Target { get; }
    public Color Color { get; }
    public TimeSpan Duration { get; }

    public PartExchangeVisualEvent(NetEntity user, NetEntity target, Color color, TimeSpan duration)
    {
        User = user;
        Target = target;
        Color = color;
        Duration = duration;
    }
}
